using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SocietyLedger.Application.DTOs.Subscription;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Persistence.Entities;
using System.Text.Json;

namespace SocietyLedger.Infrastructure.Services
{
    public class SubscriptionService : ISubscriptionService
    {
        private readonly ISubscriptionRepository _subscriptionRepo;
        private readonly IPlanRepository _planRepo;
        private readonly IInvoiceRepository _invoiceRepo;
        private readonly ISubscriptionEventRepository _eventRepo;
        private readonly AppDbContext _db;
        private readonly ILogger<SubscriptionService> _logger;

        public SubscriptionService(
            ISubscriptionRepository subscriptionRepo,
            IPlanRepository planRepo,
            IInvoiceRepository invoiceRepo,
            ISubscriptionEventRepository eventRepo,
            AppDbContext db,
            ILogger<SubscriptionService> logger)
        {
            _subscriptionRepo = subscriptionRepo;
            _planRepo = planRepo;
            _invoiceRepo = invoiceRepo;
            _eventRepo = eventRepo;
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Returns the subscription status for the society that the authenticated user belongs to.
        /// Billing is society-scoped: the subscription is looked up by society_id, not user_id.
        /// MonthlyAmount is taken from subscription.subscribed_amount — never from plan.price.
        /// </summary>
        public async Task<SubscriptionStatusResponse> GetSubscriptionStatusAsync(long userId)
        {
            // Resolve society_id from the authenticated user
            var societyId = await GetSocietyIdAsync(userId);

            // Lookup subscription by society — not by user
            var subscription = await _subscriptionRepo.GetBySocietyIdAsync(societyId);

            if (subscription == null)
                return new SubscriptionStatusResponse { Status = "none", AccessAllowed = false };

            var now = DateTime.UtcNow;
            var accessAllowed = false;
            int? trialDaysRemaining = null;

            if (subscription.Status == SubscriptionStatusCodes.Trial)
            {
                if (subscription.TrialEnd > now)
                {
                    accessAllowed = true;
                    // Use TotalDays (rounded up) so that <24 h remaining shows 1, not 0.
                    // TimeSpan.Days only counts whole-day components and returns 0 when less
                    // than 24 hours remain, incorrectly suggesting the trial has expired.
                    trialDaysRemaining = (int)Math.Ceiling((subscription.TrialEnd.Value - now).TotalDays);
                }
            }
            else if (subscription.Status == SubscriptionStatusCodes.Active)
            {
                accessAllowed = true;
            }
            else if (subscription.Status == SubscriptionStatusCodes.Cancelled)
            {
                accessAllowed = subscription.CurrentPeriodEnd > now;
            }

            return new SubscriptionStatusResponse
            {
                Status = subscription.Status,
                TrialDaysRemaining = trialDaysRemaining,
                TrialEndDate = subscription.TrialEnd,
                AccessAllowed = accessAllowed,
                PlanName = subscription.Plan?.Name,
                // Rule #1: always use subscribed_amount, never plan.price
                MonthlyAmount = subscription.SubscribedAmount,
                Currency = subscription.Currency ?? "INR"
            };
        }

        /// <summary>
        /// Subscribes a society to a plan (new subscription, renewal, or upgrade).
        /// Rules enforced:
        ///   - Plan must be active.
        ///   - Amount is always taken from plan.price — client-supplied price is ignored.
        ///   - Only one active/trial subscription per society at any time.
        ///   - For upgrades/renewals: the existing sub is expired and a brand-new one is created;
        ///     the old sub price is never mutated.
        ///   - Wrapped in a transaction with pg_advisory_xact_lock keyed by society_id to prevent
        ///     concurrent duplicate subscriptions.
        /// </summary>
        public async Task<SubscribeResponse> SubscribeAsync(long userId, SubscribeRequest request)
        {
            var plan = await _planRepo.GetByIdAsync(request.PlanId);
            if (plan == null)
                throw new NotFoundException("Plan", request.PlanId.ToString());

            // Safety check: plan must be active before allowing subscription
            if (plan.IsActive != true)
                throw new ConflictException("The selected plan is not currently active.");

            // Resolve society_id from the authenticated user
            var userEntity = await _db.users.FindAsync(userId)
                ?? throw new NotFoundException("User", userId.ToString());

            var societyId = userEntity.society_id;

            // Validate society's flat count against the plan's flat limit
            var flatsCount = await _db.flats.CountAsync(f => f.society_id == societyId && !f.is_deleted);
            if (flatsCount > plan.MaxFlats)
                throw new ConflictException(
                    $"Selected plan supports up to {plan.MaxFlats} flats, but your society has {flatsCount} active flats.");

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                // Advisory lock keyed by societyId — serialises concurrent subscribe calls for the
                // same society and prevents the partial-unique-index violation from racing threads.
                await _db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({societyId})");

                // Re-read inside the lock so the check is atomic with the write.
                var existingSubscription = await _subscriptionRepo.GetBySocietyIdAsync(societyId);

                // Guard: cannot re-subscribe while a cancelled sub's period is still running.
                // Active and trial subscriptions are handled by expiring them (upgrade/renewal).
                if (existingSubscription != null
                    && existingSubscription.Status == SubscriptionStatusCodes.Cancelled
                    && existingSubscription.CurrentPeriodEnd.HasValue
                    && existingSubscription.CurrentPeriodEnd.Value > DateTime.UtcNow)
                {
                    throw new ConflictException(
                        $"Your current subscription period is still active until " +
                        $"{existingSubscription.CurrentPeriodEnd.Value:yyyy-MM-dd}. " +
                        $"You can re-subscribe after that date.");
                }

                var now = DateTime.UtcNow;
                string eventType;

                if (existingSubscription != null
                    && (existingSubscription.Status == SubscriptionStatusCodes.Active
                     || existingSubscription.Status == SubscriptionStatusCodes.Trial))
                {
                    // Upgrade or renewal: expire the old subscription — never mutate its price.
                    existingSubscription.Status = SubscriptionStatusCodes.Expired;
                    existingSubscription.UpdatedAt = now;
                    await _subscriptionRepo.UpdateAsync(existingSubscription);
                    eventType = "renewed";
                }
                else
                {
                    eventType = "subscribed";
                }

                // Rule #1: always derive amount from plan.price — ignore any client-supplied price.
                var amount = plan.Price;

                // Rule #2: set society_id (not only user_id) on the new subscription.
                var subscription = new Subscription
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    SocietyId = societyId,
                    PlanId = request.PlanId,
                    Status = SubscriptionStatusCodes.Active,
                    SubscribedAmount = amount,
                    Currency = plan.Currency,
                    CurrentPeriodStart = now,
                    CurrentPeriodEnd = now.AddMonths(plan.DurationMonths),
                    CreatedAt = now,
                    UpdatedAt = now
                };
                await _subscriptionRepo.CreateAsync(subscription);

                // Invoice amount comes from subscription.subscribed_amount — not plan.price directly.
                var invoice = new Invoice
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    SubscriptionId = subscription.Id,
                    InvoiceType = PaymentTypeCodes.Subscription,
                    Amount = subscription.SubscribedAmount,
                    TotalAmount = subscription.SubscribedAmount,
                    Currency = subscription.Currency,
                    Status = request.PaymentMethod.Equals(PaymentModeCodes.Razorpay, StringComparison.OrdinalIgnoreCase)
                        ? InvoiceStatusCodes.Pending
                        : InvoiceStatusCodes.Paid,
                    DueDate = DateOnly.FromDateTime(now.AddDays(30)),
                    PeriodStart = DateOnly.FromDateTime(now),
                    PeriodEnd = DateOnly.FromDateTime(now.AddMonths(plan.DurationMonths)),
                    PaymentMethod = request.PaymentMethod,
                    PaymentReference = request.PaymentReference,
                    Description = $"Subscription to {plan.Name} plan"
                };
                await _invoiceRepo.CreateAsync(invoice);

                // Snapshot plan details in event metadata for a complete audit trail.
                var eventMeta = JsonSerializer.Serialize(new
                {
                    plan_id = request.PlanId,
                    plan_name = plan.Name,
                    max_flats = plan.MaxFlats,
                    duration_months = plan.DurationMonths,
                    payment_method = request.PaymentMethod,
                    society_id = societyId
                });

                await _eventRepo.CreateAsync(new SubscriptionEvent
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    SubscriptionId = subscription.Id,
                    EventType = eventType,
                    NewStatus = SubscriptionStatusCodes.Active,
                    Amount = subscription.SubscribedAmount,
                    Metadata = eventMeta
                });

                await tx.CommitAsync();

                _logger.LogInformation(
                    "Society {SocietyId} subscribed to plan {PlanId} (event: {EventType})",
                    societyId, request.PlanId, eventType);

                return new SubscribeResponse
                {
                    SubscriptionId = subscription.Id,
                    InvoiceId = invoice.Id,
                    Status = invoice.Status,
                    Amount = subscription.SubscribedAmount,
                    InvoiceNumber = invoice.InvoiceNumber,
                    PaymentUrl = null // Razorpay payment link not yet implemented
                };
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// Cancels the active or trial subscription for the society the caller belongs to.
        /// </summary>
        public async Task CancelSubscriptionAsync(long userId, CancelSubscriptionRequest request)
        {
            var societyId = await GetSocietyIdAsync(userId);

            // Lookup by society — billing is society-scoped
            var subscription = await _subscriptionRepo.GetBySocietyIdAsync(societyId);
            if (subscription == null)
                throw new NotFoundException("Subscription", $"society {societyId}");

            if (subscription.Status != SubscriptionStatusCodes.Active
                && subscription.Status != SubscriptionStatusCodes.Trial)
                throw new ConflictException("Only active or trial subscriptions can be cancelled.");

            var now = DateTime.UtcNow;
            var oldStatus = subscription.Status;
            subscription.Status = SubscriptionStatusCodes.Cancelled;
            subscription.CancelledAt = now;
            subscription.UpdatedAt = now;

            await _subscriptionRepo.UpdateAsync(subscription);

            var eventMeta = JsonSerializer.Serialize(new
            {
                reason = request.Reason,
                cancel_immediately = request.CancelImmediately,
                society_id = societyId
            });

            await _eventRepo.CreateAsync(new SubscriptionEvent
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SubscriptionId = subscription.Id,
                EventType = "cancelled",
                OldStatus = oldStatus,
                NewStatus = SubscriptionStatusCodes.Cancelled,
                Metadata = eventMeta
            });

            _logger.LogInformation("Society {SocietyId} cancelled subscription", societyId);
        }

        /// <summary>
        /// Creates a 30-day trial subscription for the society the new user belongs to.
        /// If the society already has a subscription (another admin registered earlier), this is a no-op.
        /// The unique partial index (uq_subscription_active_per_society) serves as the final DB guard.
        /// </summary>
        public async Task CreateTrialSubscriptionAsync(long userId)
        {
            var userEntity = await _db.users.FindAsync(userId);
            if (userEntity == null) return;

            var societyId = userEntity.society_id;

            // Check by society_id — only one active/trial sub per society
            var existingSubscription = await _subscriptionRepo.GetBySocietyIdAsync(societyId);
            if (existingSubscription != null)
                return;

            var plans = await _planRepo.GetActivePlansAsync();
            var defaultPlan = plans.FirstOrDefault(p => p.DurationMonths == 1) ?? plans.FirstOrDefault();

            if (defaultPlan == null)
                throw new NotFoundException("Plan", "default trial plan");

            var now = DateTime.UtcNow;

            var subscription = new Subscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SocietyId = societyId,
                PlanId = defaultPlan.Id,
                Status = SubscriptionStatusCodes.Trial,
                // Trial subscriptions have zero subscribed_amount until a paid plan is selected
                SubscribedAmount = 0,
                Currency = defaultPlan.Currency,
                TrialStart = now,
                TrialEnd = now.AddDays(30),
                CreatedAt = now,
                UpdatedAt = now
            };

            try
            {
                await _subscriptionRepo.CreateAsync(subscription);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                // Concurrent registration for the same society — another request already created the trial.
                _logger.LogInformation(
                    "Trial subscription already exists for society {SocietyId} — concurrent creation, skipping",
                    societyId);
                return;
            }

            // Snapshot plan details so the trial record is self-describing in the audit log
            var eventMeta = JsonSerializer.Serialize(new
            {
                trial_days = 30,
                plan_name = defaultPlan.Name,
                max_flats = defaultPlan.MaxFlats,
                duration_months = defaultPlan.DurationMonths,
                society_id = societyId
            });

            await _eventRepo.CreateAsync(new SubscriptionEvent
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SubscriptionId = subscription.Id,
                SocietyId = societyId,
                EventType = "trial_started",
                NewStatus = SubscriptionStatusCodes.Trial,
                Metadata = eventMeta
            });

            _logger.LogInformation("Created trial subscription for society {SocietyId}", societyId);
        }

        // ──────────────────────────────────────────────────────────────────
        // Subscription enforcement — called by API write endpoints
        // ──────────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public async Task<(bool IsValid, string? Message)> ValidateSubscriptionAsync(long userId)
        {
            var societyId = await GetSocietyIdAsync(userId);
            var subscription = await _subscriptionRepo.GetBySocietyIdAsync(societyId);
            return await ValidateSubscriptionCoreAsync(subscription, societyId);
        }

        /// <inheritdoc/>
        public async Task<(bool Allowed, string? Message)> CanPerformWriteOperationAsync(long userId)
        {
            // Write operations only require a valid subscription — no flat limit check.
            return await ValidateSubscriptionAsync(userId);
        }

        /// <inheritdoc/>
        public async Task<(bool Allowed, string? Message)> CanAddFlatAsync(long userId)
        {
            var societyId = await GetSocietyIdAsync(userId);

            // Fetch the subscription once; reuse it for both validation and plan limit check.
            var subscription = await _subscriptionRepo.GetBySocietyIdAsync(societyId);

            var (isValid, message) = await ValidateSubscriptionCoreAsync(subscription, societyId);
            if (!isValid)
                return (false, message);

            // subscription is non-null here because ValidateSubscriptionCoreAsync returned true.
            // Plan is already eager-loaded by GetBySocietyIdAsync (Include(s => s.plan)).
            var plan = subscription!.Plan ?? await _planRepo.GetByIdAsync(subscription.PlanId);
            if (plan == null)
                return (false, "Subscription plan not found. Please contact support.");

            // Race-condition-safe: compare (count + 1) against the limit rather than count >= limit,
            // ensuring that two concurrent requests cannot both slip under the threshold.
            var currentFlatCount = await _db.flats
                .CountAsync(f => f.society_id == societyId && !f.is_deleted);

            if (currentFlatCount + 1 > plan.MaxFlats)
                return (false, $"Flat limit reached ({plan.MaxFlats}). Upgrade your plan to add more flats.");

            return (true, null);
        }

        /// <summary>
        /// Core validation logic. Checks subscription existence, status, and period expiry.
        /// Persists a status change to 'expired' when an active subscription's period has lapsed.
        /// </summary>
        private async Task<(bool IsValid, string? Message)> ValidateSubscriptionCoreAsync(
            Subscription? subscription, long societyId)
        {
            if (subscription == null)
                return (false, "No active subscription. Please subscribe to continue.");

            var now = DateTime.UtcNow;

            if (subscription.Status == SubscriptionStatusCodes.Active)
            {
                if (subscription.CurrentPeriodEnd.HasValue && subscription.CurrentPeriodEnd.Value <= now)
                {
                    // Period has lapsed — persist the expired status immediately so the next request
                    // does not need to re-evaluate, and so the auth cache invalidates promptly.
                    subscription.Status = SubscriptionStatusCodes.Expired;
                    subscription.UpdatedAt = now;
                    await _subscriptionRepo.UpdateAsync(subscription);

                    _logger.LogInformation(
                        "Society {SocietyId} subscription expired at {ExpiryDate} — status updated to 'expired'",
                        societyId, subscription.CurrentPeriodEnd.Value);

                    return (false, "Your subscription has expired. Please renew to continue.");
                }

                return (true, null);
            }

            if (subscription.Status == SubscriptionStatusCodes.Trial)
            {
                if (subscription.TrialEnd.HasValue && subscription.TrialEnd.Value > now)
                    return (true, null);

                return (false, "Your subscription has expired. Please renew to continue.");
            }

            // A cancelled subscription whose paid period has not yet ended retains write access —
            // consistent with GetSubscriptionStatusAsync behaviour.
            if (subscription.Status == SubscriptionStatusCodes.Cancelled
                && subscription.CurrentPeriodEnd.HasValue
                && subscription.CurrentPeriodEnd.Value > now)
            {
                return (true, null);
            }

            return (false, "No active subscription. Please subscribe to continue.");
        }

        // ──────────────────────────────────────────────────────────────────
        // Private helpers
        // ──────────────────────────────────────────────────────────────────

        /// <summary>Resolves the society_id for the given user_id. Throws if the user does not exist.</summary>
        private async Task<long> GetSocietyIdAsync(long userId)
        {
            var societyId = await _db.users
                .Where(u => u.id == userId)
                .Select(u => (long?)u.society_id)
                .FirstOrDefaultAsync();

            if (societyId == null)
                throw new NotFoundException("User", userId.ToString());

            return societyId.Value;
        }

        private static bool IsUniqueConstraintViolation(DbUpdateException ex)
            => ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true
            || ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true
            || ex.InnerException?.Message.Contains("23505") == true; // PostgreSQL unique_violation SQLSTATE
    }
}
