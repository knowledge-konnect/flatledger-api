using SocietyLedger.Domain.Entities;

namespace SocietyLedger.Application.Interfaces.Repositories
{
    public interface ISubscriptionRepository
    {
        /// <summary>Returns the latest active or trial subscription for the given society, or null if none.</summary>
        Task<Subscription?> GetBySocietyIdAsync(long societyId);
        /// <summary>Legacy lookup by user_id — kept for background services that process all subscriptions.</summary>
        Task<Subscription?> GetByUserIdAsync(long userId);
        Task<Subscription?> GetByIdAsync(Guid id);
        Task CreateAsync(Subscription subscription);
        Task UpdateAsync(Subscription subscription);
        Task BulkUpdateAsync(IEnumerable<Subscription> subscriptions);
        Task<IEnumerable<Subscription>> GetExpiredTrialsAsync();
        Task<IEnumerable<Subscription>> GetActiveSubscriptionsAsync();
    }
}