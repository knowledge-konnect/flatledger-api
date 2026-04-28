using Microsoft.EntityFrameworkCore;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Persistence.Entities;

namespace SocietyLedger.Infrastructure.Persistence.Repositories
{
    public class SubscriptionRepository : ISubscriptionRepository
    {
        private readonly AppDbContext _db;

        public SubscriptionRepository(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Returns the latest active or trial subscription for the society.
        /// Ordered by creation date descending so the most-recent row is returned.
        /// </summary>
        public async Task<Subscription?> GetBySocietyIdAsync(long societyId)
        {
            var efSubscription = await _db.subscriptions
                .Include(s => s.plan)
                .AsNoTracking()
                .Where(s => s.society_id == societyId
                         && (s.status == SubscriptionStatusCodes.Active
                          || s.status == SubscriptionStatusCodes.Trial
                          || s.status == SubscriptionStatusCodes.Cancelled))
                .OrderByDescending(s => s.created_at)
                .FirstOrDefaultAsync();

            return efSubscription?.ToDomain();
        }

        public async Task<Subscription?> GetByUserIdAsync(long userId)
        {
            var efSubscription = await _db.subscriptions
                .Include(s => s.plan)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.user_id == userId);

            return efSubscription?.ToDomain();
        }

        public async Task<Subscription?> GetByIdAsync(Guid id)
        {
            var efSubscription = await _db.subscriptions
                .Include(s => s.plan)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.id == id);

            return efSubscription?.ToDomain();
        }

        public async Task CreateAsync(Subscription subscription)
        {
            var efSubscription = subscription.ToEntity();
            efSubscription.created_at = DateTime.UtcNow;
            efSubscription.updated_at = DateTime.UtcNow;
            _db.subscriptions.Add(efSubscription);
            await _db.SaveChangesAsync();
            subscription.Id = efSubscription.id;
        }

        public async Task UpdateAsync(Subscription subscription)
        {
            // Fetch the tracked entity and update only the mutable fields.
            // Using _db.subscriptions.Update(detachedEntity) marks every column dirty and can
            // overwrite immutable fields (e.g. created_at, user_id) not present in the domain model.
            var efSubscription = await _db.subscriptions.FirstOrDefaultAsync(s => s.id == subscription.Id);
            if (efSubscription == null) return;

            efSubscription.status               = subscription.Status;
            efSubscription.cancelled_at         = subscription.CancelledAt;
            efSubscription.current_period_end   = subscription.CurrentPeriodEnd;
            efSubscription.current_period_start = subscription.CurrentPeriodStart;
            efSubscription.trial_end            = subscription.TrialEnd;
            efSubscription.updated_at           = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        public async Task BulkUpdateAsync(IEnumerable<Subscription> subscriptions)
        {
            var subscriptionList = subscriptions.ToList();
            if (subscriptionList.Count == 0) return;

            var ids = subscriptionList.Select(s => s.Id).ToList();
            var tracked = await _db.subscriptions
                .Where(s => ids.Contains(s.id))
                .ToListAsync();

            var lookup = subscriptionList.ToDictionary(s => s.Id);
            var now = DateTime.UtcNow;

            foreach (var efSub in tracked)
            {
                if (!lookup.TryGetValue(efSub.id, out var domain)) continue;
                efSub.status     = domain.Status;
                efSub.updated_at = now;
            }

            // Single SaveChanges for all rows — avoids N round-trips.
            await _db.SaveChangesAsync();
        }

        public async Task<IEnumerable<Subscription>> GetExpiredTrialsAsync()
        {
            var now = DateTime.UtcNow;
            var efSubscriptions = await _db.subscriptions
                .Include(s => s.plan)
                .Where(s => s.status == SubscriptionStatusCodes.Trial && s.trial_end < now)
                .AsNoTracking()
                .ToListAsync();

            return efSubscriptions.Select(s => s.ToDomain());
        }

        public async Task<IEnumerable<Subscription>> GetActiveSubscriptionsAsync()
        {
            var efSubscriptions = await _db.subscriptions
                .Include(s => s.plan)
                .Where(s => s.status == SubscriptionStatusCodes.Active)
                .AsNoTracking()
                .ToListAsync();

            return efSubscriptions.Select(s => s.ToDomain());
        }
    }
}