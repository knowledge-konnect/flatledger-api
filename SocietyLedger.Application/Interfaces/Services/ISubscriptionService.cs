using SocietyLedger.Application.DTOs.Subscription;

namespace SocietyLedger.Application.Interfaces.Services
{
    public interface ISubscriptionService
    {
        Task<SubscriptionStatusResponse> GetSubscriptionStatusAsync(long userId);
        Task<SubscribeResponse> SubscribeAsync(long userId, SubscribeRequest request);
        Task CancelSubscriptionAsync(long userId, CancelSubscriptionRequest request);
        Task CreateTrialSubscriptionAsync(long userId);

        /// <summary>
        /// Checks whether the society's subscription is currently active and not expired.
        /// If an active subscription has lapsed, its status is updated to 'expired' in the DB.
        /// </summary>
        /// <returns>(true, null) if valid; (false, reason) if not.</returns>
        Task<(bool IsValid, string? Message)> ValidateSubscriptionAsync(long userId);

        /// <summary>
        /// Checks both subscription validity and whether the plan's flat limit allows
        /// adding one more flat to the society.
        /// </summary>
        /// <returns>(true, null) if allowed; (false, reason) if not.</returns>
        Task<(bool Allowed, string? Message)> CanAddFlatAsync(long userId);

        /// <summary>
        /// Checks both subscription validity and whether the plan's flat limit allows
        /// adding <paramref name="count"/> flats to the society in a single operation.
        /// Used by the bulk-create endpoint so the entire batch is validated upfront.
        /// </summary>
        /// <returns>(true, null) if allowed; (false, reason) if not.</returns>
        Task<(bool Allowed, string? Message)> CanAddFlatsAsync(long userId, int count);

        /// <summary>
        /// Checks whether write operations (bills, payments, expenses) are permitted
        /// based on an active subscription. Equivalent to ValidateSubscriptionAsync.
        /// </summary>
        /// <returns>(true, null) if allowed; (false, reason) if not.</returns>
        Task<(bool Allowed, string? Message)> CanPerformWriteOperationAsync(long userId);
    }
}