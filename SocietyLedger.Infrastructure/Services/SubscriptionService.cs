using Microsoft.Extensions.Logging;
using SocietyLedger.Application.DTOs.Subscription;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Domain.Exceptions;

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
        /// Returns the subscription status for the user's society (shared across admins).
        /// </summary>
        public async Task<SubscriptionStatusResponse> GetSubscriptionStatusAsync(long userId)
        {
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null)
            {
                return new SubscriptionStatusResponse
                {
                    Status = "none",
                    AccessAllowed = false
                };
            }

            var subscription = await _subscriptionRepo.GetBySocietyIdAsync(user.SocietyId)
                ?? await _subscriptionRepo.GetByUserIdAsync(userId);

            if (subscription == null)
            {
                return new SubscriptionStatusResponse
                {
                    Status = "none",
                    AccessAllowed = false
                };
            }

            return MapToStatusResponse(subscription);
        }

        /// <summary>
        /// Subscribes a user to a plan, blocks re-subscribe if paid period is still active, creates invoice atomically.
        /// </summary>
        public async Task<SubscribeResponse> SubscribeAsync(long userId, SubscribeRequest request)
        {
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null)
                throw new NotFoundException("User", userId.ToString());

            var plan = await _planRepo.GetByIdAsync(request.PlanId);
            if (plan == null)
                throw new NotFoundException("Plan", request.PlanId.ToString());

            var existingSubscription = await _subscriptionRepo.GetBySocietyIdAsync(user.SocietyId)
                ?? await _subscriptionRepo.GetByUserIdAsync(userId);

            if (existingSubscription != null && existingSubscription.Status == SubscriptionStatusCodes.Active)
                throw new ConflictException("This society already has an active subscription.");

            if (existingSubscription != null
                && existingSubscription.Status != SubscriptionStatusCodes.Active
                && existingSubscription.CurrentPeriodEnd.HasValue
                && existingSubscription.CurrentPeriodEnd.Value > DateTime.UtcNow)
                throw new ConflictException(
                    $"Your current subscription period is still active until {existingSubscription.CurrentPeriodEnd.Value:yyyy-MM-dd}. " +
                    $"You can re-subscribe after that date.");

            var amount = request.Amount ?? plan.Price;
            var now = DateTime.UtcNow;
            var durationMonths = plan.DurationMonths > 0 ? plan.DurationMonths : 1;
            var periodEnd = now.AddMonths(durationMonths);
            var isPrepaidRazorpay = request.PaymentMethod.Equals(PaymentModeCodes.Razorpay, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(request.PaymentReference);

            Subscription subscription;
            if (existingSubscription != null)
            {
                subscription = existingSubscription;
                subscription.UserId = userId;
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
                    SocietyId = user.SocietyId,
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

            var invoice = new Invoice
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SubscriptionId = subscription.Id,
                InvoiceType = PaymentTypeCodes.Subscription,
                Amount = amount,
                TotalAmount = amount,
                Currency = plan.Currency,
                Status = isPrepaidRazorpay ? InvoiceStatusCodes.Paid : InvoiceStatusCodes.Pending,
                DueDate = DateOnly.FromDateTime(now.AddDays(30)),
                PaymentMethod = request.PaymentMethod,
                PaymentReference = request.PaymentReference,
                PaidDate = isPrepaidRazorpay ? now : null,
                Description = $"Subscription to {plan.Name} plan"
            };
            await _invoiceRepo.CreateAsync(invoice);

            await _eventRepo.CreateAsync(new SubscriptionEvent
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SocietyId = user.SocietyId,
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
                PaymentUrl = request.PaymentMethod.ToLower() == PaymentModeCodes.Razorpay ? "https://api.razorpay.com/v1/payment_links" : null
            };
        }

        public async Task CancelSubscriptionAsync(long userId, CancelSubscriptionRequest request)
        {
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null)
                throw new NotFoundException("User", userId.ToString());

            var subscription = await _subscriptionRepo.GetBySocietyIdAsync(user.SocietyId)
                ?? await _subscriptionRepo.GetByUserIdAsync(userId);
            if (subscription == null)
                throw new NotFoundException("Subscription", userId.ToString());

            if (subscription.Status != SubscriptionStatusCodes.Active
                && subscription.Status != SubscriptionStatusCodes.Trial)
                throw new ConflictException("Only active or trial subscriptions can be cancelled.");

            var now = DateTime.UtcNow;
            var oldStatus = subscription.Status;
            subscription.Status = SubscriptionStatusCodes.Cancelled;
            subscription.CancelledAt = now;
            subscription.UpdatedAt = now;

            await _subscriptionRepo.UpdateAsync(subscription);

            await _eventRepo.CreateAsync(new SubscriptionEvent
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SocietyId = user.SocietyId,
                SubscriptionId = subscription.Id,
                EventType = "cancelled",
                OldStatus = oldStatus,
                NewStatus = SubscriptionStatusCodes.Cancelled,
                Metadata = $"{{\"reason\":\"{request.Reason}\",\"cancel_immediately\":{request.CancelImmediately.ToString().ToLower()}}}"
            });

            _logger.LogInformation("User {UserId} cancelled subscription for society {SocietyId}", userId, user.SocietyId);
        }

        public async Task CreateTrialSubscriptionAsync(long userId)
        {
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null)
                throw new NotFoundException("User", userId.ToString());

            var existingSubscription = await _subscriptionRepo.GetBySocietyIdAsync(user.SocietyId)
                ?? await _subscriptionRepo.GetByUserIdAsync(userId);
            if (existingSubscription != null)
                return;

            var plans = await _planRepo.GetActivePlansAsync();
            var defaultPlan = plans.FirstOrDefault(p => p.Name.Contains("Basic", StringComparison.OrdinalIgnoreCase))
                ?? plans.OrderBy(p => p.DisplayOrder).FirstOrDefault();

            if (defaultPlan == null)
                throw new NotFoundException("Plan", "default trial plan");

            var now = DateTime.UtcNow;
            var trialEnd = now.AddDays(30);

            var subscription = new Subscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SocietyId = user.SocietyId,
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

            await _eventRepo.CreateAsync(new SubscriptionEvent
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SocietyId = user.SocietyId,
                SubscriptionId = subscription.Id,
                EventType = "trial_started",
                NewStatus = SubscriptionStatusCodes.Trial,
                Metadata = $"{{\"trial_days\":30}}"
            });

            _logger.LogInformation("Created trial subscription for user {UserId} in society {SocietyId}", userId, user.SocietyId);
        }

        private static SubscriptionStatusResponse MapToStatusResponse(Subscription subscription)
        {
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
                accessAllowed = subscription.CurrentPeriodEnd > now;
            }

            var subscribedAmount = subscription.SubscribedAmount > 0 ? subscription.SubscribedAmount : (decimal?)null;
            var planAmount = subscription.Plan?.Price > 0
                ? subscription.Plan.Price
                : subscription.Plan?.MonthlyAmount > 0
                    ? subscription.Plan.MonthlyAmount
                    : (decimal?)null;
            var amountSource = subscription.Status == SubscriptionStatusCodes.Trial
                ? "trial"
                : subscribedAmount.HasValue
                    ? "subscribed"
                    : planAmount.HasValue
                        ? "plan"
                        : "unknown";

            return new SubscriptionStatusResponse
            {
                Status = subscription.Status,
                TrialDaysRemaining = trialDaysRemaining,
                TrialEndDate = subscription.TrialEnd,
                AccessAllowed = accessAllowed,
                PlanName = subscription.Plan?.Name,
                MonthlyAmount = subscribedAmount ?? planAmount,
                SubscribedAmount = subscribedAmount,
                PlanMonthlyAmount = planAmount,
                CurrentPeriodEnd = subscription.CurrentPeriodEnd,
                AmountSource = amountSource,
                Currency = subscription.Plan?.Currency ?? "INR",
                DurationMonths = subscription.Plan?.DurationMonths > 0 ? subscription.Plan.DurationMonths : 1,
                MaxFlats = subscription.Plan?.MaxFlats > 0 ? subscription.Plan.MaxFlats : null
            };
        }
    }
}
