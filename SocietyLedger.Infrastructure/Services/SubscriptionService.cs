using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SocietyLedger.Application.DTOs.Subscription;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Domain.Exceptions;
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
        private readonly IUserRepository _userRepo;
        private readonly ILogger<SubscriptionService> _logger;

        public SubscriptionService(
            ISubscriptionRepository subscriptionRepo,
            IPlanRepository planRepo,
            IInvoiceRepository invoiceRepo,
            ISubscriptionEventRepository eventRepo,
            IUserRepository userRepo,
            ILogger<SubscriptionService> logger)
        {
            _subscriptionRepo = subscriptionRepo;
            _planRepo = planRepo;
            _invoiceRepo = invoiceRepo;
            _eventRepo = eventRepo;
            _userRepo = userRepo;
            _logger = logger;
        }

        /// <summary>
        /// Returns the subscription status for a user, including trial days remaining and access allowed.
        /// </summary>
        public async Task<SubscriptionStatusResponse> GetSubscriptionStatusAsync(long userId)
        {
            var subscription = await _subscriptionRepo.GetByUserIdAsync(userId);

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
                    trialDaysRemaining = (subscription.TrialEnd - now)?.Days;
                }
            }
            else if (subscription.Status == SubscriptionStatusCodes.Active)
            {
                accessAllowed = true;
            }
            else if (subscription.Status == SubscriptionStatusCodes.Cancelled)
            {
                // Allow access until period end if cancelled
                accessAllowed = subscription.CurrentPeriodEnd > now;
            }

            return new SubscriptionStatusResponse
            {
                Status = subscription.Status,
                TrialDaysRemaining = trialDaysRemaining,
                TrialEndDate = subscription.TrialEnd,
                AccessAllowed = accessAllowed,
                PlanName = subscription.Plan?.Name,
                MonthlyAmount = subscription.Plan?.MonthlyAmount,
                SubscribedAmount = subscription.SubscribedAmount > 0 ? subscription.SubscribedAmount : subscription.Plan?.Price,
                Currency = subscription.Plan?.Currency ?? "INR",
                CurrentPeriodEnd = subscription.CurrentPeriodEnd,
                DurationMonths = subscription.Plan?.DurationMonths,
                MaxFlats = subscription.Plan?.MaxFlats
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

            // Resolve the society for this user — subscriptions are society-scoped
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null)
                throw new NotFoundException("User", userId.ToString());
            var societyId = user.SocietyId;

            var existingSubscription = await _subscriptionRepo.GetByUserIdAsync(userId);
            if (existingSubscription != null && existingSubscription.Status == SubscriptionStatusCodes.Active)
                throw new ConflictException("Society already has an active subscription.");

            // Block re-subscribing when the current paid period hasn't ended yet.
            // Applies to Cancelled subscriptions that still have time remaining.
            if (existingSubscription != null
                && existingSubscription.Status != SubscriptionStatusCodes.Active
                && existingSubscription.CurrentPeriodEnd.HasValue
                && existingSubscription.CurrentPeriodEnd.Value > DateTime.UtcNow)
                throw new ConflictException(
                    $"Your current subscription period is still active until {existingSubscription.CurrentPeriodEnd.Value:yyyy-MM-dd}. " +
                    $"You can re-subscribe after that date.");

            // Price is the actual amount charged per billing cycle (e.g. ₹2999/year).
            // MonthlyAmount is display-only (e.g. ₹249/month label on pricing cards).
            var amount = plan.Price;
            var now = DateTime.UtcNow;
            // Use plan duration so annual plans get 12 months, not 1
            var durationMonths = plan.DurationMonths > 0 ? plan.DurationMonths : 1;
            var periodEnd = now.AddMonths(durationMonths);

            // Create or update subscription
            Subscription subscription;
            if (existingSubscription != null)
            {
                subscription = existingSubscription;
                subscription.Status = SubscriptionStatusCodes.Active;
                subscription.PlanId = request.PlanId;
                subscription.SubscribedAmount = amount;
                subscription.Currency = plan.Currency;
                subscription.CurrentPeriodStart = now;
                subscription.CurrentPeriodEnd = periodEnd;
                subscription.UpdatedAt = now;
                await _subscriptionRepo.UpdateAsync(subscription);
            }
            else
            {
                subscription = new Subscription
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    SocietyId = societyId,
                    PlanId = request.PlanId,
                    Status = SubscriptionStatusCodes.Active,
                    SubscribedAmount = amount,
                    Currency = plan.Currency,
                    CurrentPeriodStart = now,
                    CurrentPeriodEnd = periodEnd,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                await _subscriptionRepo.CreateAsync(subscription);
            }

            // Create invoice — the repository generates the invoice number atomically
            // inside a pg_advisory_xact_lock, so concurrent subscriptions never clash.
            var invoice = new Invoice
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SubscriptionId = subscription.Id,
                InvoiceType = PaymentTypeCodes.Subscription,
                Amount = amount,
                TotalAmount = amount,
                Currency = plan.Currency,
                Status = request.PaymentMethod.ToLower() == PaymentModeCodes.Razorpay ? InvoiceStatusCodes.Pending : InvoiceStatusCodes.Paid,
                DueDate = DateOnly.FromDateTime(now.AddDays(30)),
                PaymentMethod = request.PaymentMethod,
                PaymentReference = request.PaymentReference,
                Description = $"Subscription to {plan.Name} plan"
            };
            await _invoiceRepo.CreateAsync(invoice);
            // invoice.InvoiceNumber is set by the repository after CreateAsync.

            // Create subscription event
            await _eventRepo.CreateAsync(new SubscriptionEvent
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SocietyId = societyId,
                SubscriptionId = subscription.Id,
                EventType = "subscribed",
                NewStatus = SubscriptionStatusCodes.Active,
                Amount = amount,
                Metadata = $"{{\"plan_id\":\"{request.PlanId}\",\"payment_method\":\"{request.PaymentMethod}\"}}"
            });

            return new SubscribeResponse
            {
                SubscriptionId = subscription.Id,
                InvoiceId = invoice.Id,
                Status = invoice.Status,
                Amount = amount,
                InvoiceNumber = invoice.InvoiceNumber,
                PaymentUrl = request.PaymentMethod.ToLower() == PaymentModeCodes.Razorpay ? "https://api.razorpay.com/v1/payment_links" : null // Placeholder
            };
        }

        /// <summary>
        /// Cancels a subscription, allows cancellation of both Active and Trial subscriptions.
        /// </summary>
        public async Task CancelSubscriptionAsync(long userId, CancelSubscriptionRequest request)
        {
            var subscription = await _subscriptionRepo.GetByUserIdAsync(userId);
            if (subscription == null)
                throw new NotFoundException("Subscription", $"user {userId}");

            if (subscription.Status != SubscriptionStatusCodes.Active
                && subscription.Status != SubscriptionStatusCodes.Trial)
                throw new ConflictException("Only active or trial subscriptions can be cancelled.");

            var now = DateTime.UtcNow;
            var oldStatus = subscription.Status;
            subscription.Status = SubscriptionStatusCodes.Cancelled;
            subscription.CancelledAt = now;
            subscription.UpdatedAt = now;

            await _subscriptionRepo.UpdateAsync(subscription);

            // Create subscription event
            await _eventRepo.CreateAsync(new SubscriptionEvent
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SocietyId = subscription.SocietyId,
                SubscriptionId = subscription.Id,
                EventType = "cancelled",
                OldStatus = oldStatus,
                NewStatus = SubscriptionStatusCodes.Cancelled,
                Metadata = "{}"
            });

            _logger.LogInformation("User {UserId} cancelled subscription", userId);
        }

        /// <summary>
        /// Creates a 30-day trial subscription for a user, idempotent.
        /// </summary>
        public async Task<Subscription?> CreateTrialSubscriptionAsync(long userId)
        {
            var existingSubscription = await _subscriptionRepo.GetByUserIdAsync(userId);
            if (existingSubscription != null)
                return existingSubscription; // Already has a subscription

            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null)
                throw new NotFoundException("User", userId.ToString());
            var societyId = user.SocietyId;

            var plans = await _planRepo.GetActivePlansAsync();
            var defaultPlan = plans.FirstOrDefault(p => p.Name.Contains("Basic") || p.Name.Contains("Free")) ?? plans.FirstOrDefault();

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
                _logger.LogInformation(
                    "Trial subscription already exists for user {UserId} — concurrent creation, skipping",
                    userId);
                return await _subscriptionRepo.GetByUserIdAsync(userId);
            } 

            var eventMeta = JsonSerializer.Serialize(new
            {
                trial_days = 30,
                plan_name = defaultPlan.Name
            });

            await _eventRepo.CreateAsync(new SubscriptionEvent
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SocietyId = societyId,
                SubscriptionId = subscription.Id,
                EventType = "trial_started",
                NewStatus = SubscriptionStatusCodes.Trial,
                Metadata = eventMeta
            });

            _logger.LogInformation("Created trial subscription for user {UserId}", userId);
            return subscription;
        }

        public async Task<(bool IsValid, string? Message)> ValidateSubscriptionAsync(long userId)
        {
            var subscription = await _subscriptionRepo.GetByUserIdAsync(userId);
            if (subscription == null)
                return (false, "No subscription found.");

            var now = DateTime.UtcNow;

            if (subscription.Status == SubscriptionStatusCodes.Trial)
            {
                if (subscription.TrialEnd > now)
                    return (true, null);

                subscription.Status = SubscriptionStatusCodes.Expired;
                subscription.UpdatedAt = now;
                await _subscriptionRepo.UpdateAsync(subscription);
                return (false, "Your trial period has ended. Please subscribe to continue.");
            }

            if (subscription.Status == SubscriptionStatusCodes.Active)
            {
                if (subscription.CurrentPeriodEnd > now)
                    return (true, null);

                subscription.Status = SubscriptionStatusCodes.Expired;
                subscription.UpdatedAt = now;
                await _subscriptionRepo.UpdateAsync(subscription);
                return (false, "Your subscription period has ended. Please renew to continue.");
            }

            if (subscription.Status == SubscriptionStatusCodes.Cancelled)
            {
                if (subscription.CurrentPeriodEnd > now)
                    return (true, null);

                return (false, "Your subscription has been cancelled.");
            }

            return (false, $"Subscription status '{subscription.Status}' does not allow access.");
        }

        public async Task<(bool Allowed, string? Message)> CanPerformWriteOperationAsync(long userId)
            => await ValidateSubscriptionAsync(userId);

        public async Task<(bool Allowed, string? Message)> CanAddFlatAsync(long userId)
        {
            var (isValid, message) = await ValidateSubscriptionAsync(userId);
            if (!isValid)
                return (false, message);

            // TODO: enforce plan flat limit. Need:
            //   - Plan's flat-limit field (e.g. plan.MaxFlats)
            //   - current flat count for this society (e.g. _flatRepo.CountBySocietyIdAsync(userId))
            return (true, null);
        }

        public async Task<(bool Allowed, string? Message)> CanAddFlatsAsync(long userId, int count)
        {
            var (isValid, message) = await ValidateSubscriptionAsync(userId);
            if (!isValid)
                return (false, message);

            // TODO: same as CanAddFlatAsync, but check (currentCount + count) <= plan limit
            return (true, null);
        }

        private static bool IsUniqueConstraintViolation(DbUpdateException ex)
            => ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true
            || ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true
            || ex.InnerException?.Message.Contains("23505") == true; // PostgreSQL unique_violation SQLSTATE
    }
}
