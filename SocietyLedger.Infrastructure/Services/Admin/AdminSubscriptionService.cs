using Microsoft.EntityFrameworkCore;
using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Application.Interfaces.Services.Admin;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Shared;

namespace SocietyLedger.Infrastructure.Services.Admin
{
    public class AdminSubscriptionService : IAdminSubscriptionService
    {
        private readonly AppDbContext _db;
        public AdminSubscriptionService(AppDbContext db) { _db = db; }

        public async Task<PagedResult<AdminSubscriptionDto>> GetSubscriptionsAsync(int page, int pageSize, string? status = null, long? userId = null)
        {
            var query = _db.subscriptions
                .AsNoTracking()
                .Include(s => s.user)
                .Include(s => s.plan)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(s => s.status == status);
            if (userId.HasValue)
                query = query.Where(s => s.user_id == userId);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(s => s.created_at)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new AdminSubscriptionDto
                {
                    Id = s.id,
                    UserId = s.user_id,
                    UserName = s.user.name,
                    UserEmail = s.user.email,
                    PlanId = s.plan_id,
                    PlanName = s.plan.name,
                    Status = s.status,
                    SubscribedAmount = s.subscribed_amount,
                    Currency = s.currency,
                    CurrentPeriodStart = s.current_period_start,
                    CurrentPeriodEnd = s.current_period_end,
                    TrialStart = s.trial_start,
                    TrialEnd = s.trial_end,
                    CancelledAt = s.cancelled_at,
                    CreatedAt = s.created_at,
                    UpdatedAt = s.updated_at
                })
                .ToListAsync();

            return new PagedResult<AdminSubscriptionDto>(items, total, page, pageSize);
        }

        public async Task<AdminSubscriptionDto?> GetSubscriptionByIdAsync(Guid id)
        {
            return await _db.subscriptions
                .AsNoTracking()
                .Include(s => s.user)
                .Include(s => s.plan)
                .Where(s => s.id == id)
                .Select(s => new AdminSubscriptionDto
                {
                    Id = s.id,
                    UserId = s.user_id,
                    UserName = s.user.name,
                    UserEmail = s.user.email,
                    PlanId = s.plan_id,
                    PlanName = s.plan.name,
                    Status = s.status,
                    SubscribedAmount = s.subscribed_amount,
                    Currency = s.currency,
                    CurrentPeriodStart = s.current_period_start,
                    CurrentPeriodEnd = s.current_period_end,
                    TrialStart = s.trial_start,
                    TrialEnd = s.trial_end,
                    CancelledAt = s.cancelled_at,
                    CreatedAt = s.created_at,
                    UpdatedAt = s.updated_at
                })
                .FirstOrDefaultAsync();
        }

        public async Task<AdminSubscriptionDto> UpdateSubscriptionAsync(Guid id, AdminSubscriptionUpdateRequest request)
        {
            var sub = await _db.subscriptions.FirstOrDefaultAsync(x => x.id == id);
            if (sub == null) throw new NotFoundException("Subscription", id.ToString());

            sub.plan_id = request.PlanId;
            sub.status = request.Status;
            sub.current_period_start = request.CurrentPeriodStart;
            sub.current_period_end = request.CurrentPeriodEnd;
            sub.updated_at = DateTime.UtcNow;
            if (request.Status == "cancelled")
                sub.cancelled_at = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return await GetSubscriptionByIdAsync(sub.id) ?? throw new Exception("Failed to update subscription");
        }
    }
}
