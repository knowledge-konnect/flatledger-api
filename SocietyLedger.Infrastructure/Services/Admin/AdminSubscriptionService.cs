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

        /// <summary>
        /// Returns a paged list of subscriptions. Can be filtered by status and/or society_id.
        /// Billing is society-based, so society_id is the primary filter — userId is kept for
        /// backward compatibility with existing admin queries.
        /// </summary>
        public async Task<PagedResult<AdminSubscriptionDto>> GetSubscriptionsAsync(
            int page, int pageSize, string? status = null, long? userId = null, long? societyId = null)
        {
            var query = _db.subscriptions
                .AsNoTracking()
                .Include(s => s.user)
                .Include(s => s.plan)
                .Include(s => s.society)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(s => s.status == status);

            // Prefer society_id filter; fall back to user_id for legacy callers
            if (societyId.HasValue)
                query = query.Where(s => s.society_id == societyId);
            else if (userId.HasValue)
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
                    SocietyId = s.society_id,
                    SocietyName = s.society.name,
                    PlanId = s.plan_id,
                    PlanName = s.plan.name,
                    Status = s.status,
                    // SubscribedAmount is the canonical billing amount—never read plan.price here
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
                .Include(s => s.society)
                .Where(s => s.id == id)
                .Select(s => new AdminSubscriptionDto
                {
                    Id = s.id,
                    UserId = s.user_id,
                    UserName = s.user.name,
                    UserEmail = s.user.email,
                    SocietyId = s.society_id,
                    SocietyName = s.society.name,
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

    }
}
