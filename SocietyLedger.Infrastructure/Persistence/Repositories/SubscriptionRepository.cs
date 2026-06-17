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

        public async Task<Subscription?> GetByUserIdAsync(long userId)
        {
            var efSubscription = await _db.subscriptions
                .Include(s => s.plan)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.user_id == userId);

            return efSubscription?.ToDomain();
        }

        public async Task<Subscription?> GetBySocietyIdAsync(long societyId)
        {
            var efSubscriptions = await _db.subscriptions
                .Include(s => s.plan)
                .AsNoTracking()
                .Where(s => s.society_id == societyId)
                .ToListAsync();

            if (efSubscriptions.Count == 0)
                return null;

            var now = DateTime.UtcNow;
            var best = efSubscriptions
                .OrderByDescending(s => ScoreSubscription(s, now))
                .ThenByDescending(s => s.updated_at ?? s.created_at)
                .First();

            return best.ToDomain();
        }

        private static int ScoreSubscription(subscription s, DateTime now)
        {
            if (s.status == SubscriptionStatusCodes.Active) return 1000;
            if (s.status == SubscriptionStatusCodes.Trial && s.trial_end > now) return 900;
            if (s.status == SubscriptionStatusCodes.Cancelled && s.current_period_end > now) return 800;
            if (s.status == SubscriptionStatusCodes.Trial) return 100;
            return 0;
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
            var efSubscription = subscription.ToEntity();
            efSubscription.updated_at = DateTime.UtcNow;
            _db.subscriptions.Update(efSubscription);
            await _db.SaveChangesAsync();
        }

        public async Task<IEnumerable<Subscription>> GetExpiredTrialsAsync()
        {
            var now = DateTime.UtcNow;
            var efSubscriptions = await _db.subscriptions
                .Include(s => s.plan)
                .AsNoTracking()
                .Where(s => s.status == SubscriptionStatusCodes.Trial && s.trial_end < now)
                .ToListAsync();

            return efSubscriptions.Select(s => s.ToDomain());
        }

        public async Task<IEnumerable<Subscription>> GetActiveSubscriptionsAsync()
        {
            var efSubscriptions = await _db.subscriptions
                .Include(s => s.plan)
                .AsNoTracking()
                .Where(s => s.status == SubscriptionStatusCodes.Active)
                .ToListAsync();

            return efSubscriptions.Select(s => s.ToDomain());
        }
    }
}