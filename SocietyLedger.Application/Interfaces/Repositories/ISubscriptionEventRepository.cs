using SocietyLedger.Domain.Entities;

namespace SocietyLedger.Application.Interfaces.Repositories
{
    public interface ISubscriptionEventRepository
    {
        Task CreateAsync(SubscriptionEvent subscriptionEvent);
        Task BulkCreateAsync(IEnumerable<SubscriptionEvent> events);
        Task<IEnumerable<SubscriptionEvent>> GetByUserIdAsync(long userId);
        Task<bool> ExistsAsync(Guid subscriptionId, string eventType);
    }
}