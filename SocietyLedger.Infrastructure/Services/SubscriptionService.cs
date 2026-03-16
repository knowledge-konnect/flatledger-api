using Microsoft.Extensions.Logging;
using SocietyLedger.Application.DTOs.Subscription;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Persistence.Entities;

namespace SocietyLedger.Infrastructure.Services
{
    public class SubscriptionService : ISubscriptionService
    {
        private readonly ISubscriptionRepository _subscriptionRepo;
        private readonly IPlanRepository _planRepo;
        private readonly IInvoiceRepository _invoiceRepo;
        private readonly ISubscriptionEventRepository _eventRepo;
        private readonly ILogger<SubscriptionService> _logger;

        public SubscriptionService(
            ISubscriptionRepository subscriptionRepo,
            IPlanRepository planRepo,
            IInvoiceRepository invoiceRepo,
            ISubscriptionEventRepository eventRepo,
            ILogger<SubscriptionService> logger)
        {
            _subscriptionRepo = subscriptionRepo;
            _planRepo = planRepo;
            _invoiceRepo = invoiceRepo;
            _eventRepo = eventRepo;
            _logger = logger;
        }

        /// <summary>
        /// Returns the subscription status for a user, including trial days remaining and access allowed.
        /// </summary>
        public async Task<SubscriptionStatusResponse> GetSubscriptionStatusAsync(long userId)
        {
            var subscription = await _subscriptionRepo.GetByUserIdAsync(userId);

            if (subscription == null)
            {
                return new SubscriptionStatusResponse
                {
                    Status = "none",
                    AccessAllowed = false
                };
            }

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
                else
                {
                    accessAllowed = false; // Trial expired
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
                Currency = subscription.Plan?.Currency ?? "INR"
            };
        }

        /// <summary>
        /// Subscribes a user to a plan, blocks re-subscribe if paid period is still active, creates invoice atomically.
        /// </summary>
        public async Task<SubscribeResponse> SubscribeAsync(long userId, SubscribeRequest request)
        {
            var plan = await _planRepo.GetByIdAsync(request.PlanId);
            if (plan == null)
                throw new NotFoundException("Plan", request.PlanId.ToString());

            var existingSubscription = await _subscriptionRepo.GetByUserIdAsync(userId);
            if (existingSubscription != null && existingSubscription.Status == SubscriptionStatusCodes.Active)
                throw new ConflictException("User already has an active subscription.");

            // #9 — Block re-subscribing when the current paid period hasn't ended yet.
            //       Applies to Cancelled subscriptions that still have time remaining.
            if (existingSubscription != null
                && existingSubscription.Status != SubscriptionStatusCodes.Active
                && existingSubscription.CurrentPeriodEnd.HasValue
                && existingSubscription.CurrentPeriodEnd.Value > DateTime.UtcNow)
                throw new ConflictException(
                    $"Your current subscription period is still active until {existingSubscription.CurrentPeriodEnd.Value:yyyy-MM-dd}. " +
                    $"You can re-subscribe after that date.");

            var amount = request.Amount ?? plan.MonthlyAmount;
            var now = DateTime.UtcNow;

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
                subscription.CurrentPeriodEnd = now.AddMonths(1);
                subscription.UpdatedAt = now;
                await _subscriptionRepo.UpdateAsync(subscription);
            }
            else
            {
                subscription = new Subscription
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    PlanId = request.PlanId,
                    Status = SubscriptionStatusCodes.Active,
                    SubscribedAmount = amount,
                    Currency = plan.Currency,
                    CurrentPeriodStart = now,
                    CurrentPeriodEnd = now.AddMonths(1),
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
                SubscriptionId = subscription.Id,
                EventType = "subscribed",
                NewStatus = SubscriptionStatusCodes.Active,
                Amount = amount,
                Metadata = $"{{\"plan_id\":\"{request.PlanId}\",\"payment_method\":\"{request.PaymentMethod}\"}}"
            });

            _logger.LogInformation("User {UserId} subscribed to plan {PlanId} with payment method {PaymentMethod}", userId, request.PlanId, request.PaymentMethod);

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
                throw new NotFoundException("Subscription", userId.ToString());

            // #8 — Allow cancellation of both Active and Trial subscriptions.
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
                SubscriptionId = subscription.Id,
                EventType = "cancelled",
                OldStatus = oldStatus,
                NewStatus = SubscriptionStatusCodes.Cancelled,
                Metadata = $"{{\"reason\":\"{request.Reason}\",\"cancel_immediately\":{request.CancelImmediately.ToString().ToLower()}}}"
            });

            _logger.LogInformation("User {UserId} cancelled subscription", userId);
        }

        /// <summary>
        /// Creates a 30-day trial subscription for a user, idempotent.
        /// </summary>
        public async Task CreateTrialSubscriptionAsync(long userId)
        {
            var existingSubscription = await _subscriptionRepo.GetByUserIdAsync(userId);
            if (existingSubscription != null)
                return; // Already has a subscription

            // Get the default plan (assuming there's a basic plan)
            var plans = await _planRepo.GetActivePlansAsync();
            var defaultPlan = plans.FirstOrDefault(p => p.Name.Contains("Basic") || p.Name.Contains("Free")) ?? plans.FirstOrDefault();

            if (defaultPlan == null)
                throw new NotFoundException("Plan", "default trial plan");

            var now = DateTime.UtcNow;
            var trialEnd = now.AddDays(30);

            var subscription = new Subscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PlanId = defaultPlan.Id,
                Status = SubscriptionStatusCodes.Trial,
                SubscribedAmount = 0,
                Currency = defaultPlan.Currency,
                TrialStart = now,
                TrialEnd = trialEnd,
                CreatedAt = now,
                UpdatedAt = now
            };

            await _subscriptionRepo.CreateAsync(subscription);

            // Create subscription event
            await _eventRepo.CreateAsync(new SubscriptionEvent
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SubscriptionId = subscription.Id,
                EventType = "trial_started",
                NewStatus = SubscriptionStatusCodes.Trial,
                Metadata = $"{{\"trial_days\":30}}"
            });

            _logger.LogInformation("Created trial subscription for user {UserId}", userId);
        }
    }
}