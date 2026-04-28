using Microsoft.EntityFrameworkCore;
using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Application.Interfaces.Services.Admin;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Persistence.Entities;
using SocietyLedger.Shared;

namespace SocietyLedger.Infrastructure.Services.Admin
{
    public class AdminSocietyService : IAdminSocietyService
    {
        private const int MaxPageSize = 200;
        private readonly AppDbContext _db;
        public AdminSocietyService(AppDbContext db) { _db = db; }

        public async Task<PagedResult<AdminSocietyDto>> GetSocietiesAsync(int page, int pageSize, string? search = null)
        {
            pageSize = Math.Min(pageSize, MaxPageSize);
            var query = _db.societies.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(s => EF.Functions.ILike(s.name, $"%{search}%"));
            var total = await query.CountAsync();
            var items = await query.OrderByDescending(s => s.created_at)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new AdminSocietyDto
                {
                    Id = s.id,
                    PublicId = s.public_id,
                    Name = s.name,
                    Address = s.address,
                    City = s.city,
                    State = s.state,
                    Pincode = s.pincode,
                    Currency = s.currency,
                    DefaultMaintenanceCycle = s.default_maintenance_cycle,
                    CreatedAt = s.created_at,
                    UpdatedAt = s.updated_at,
                    IsDeleted = s.is_deleted,
                    DeletedAt = s.deleted_at,
                    OnboardingDate = s.onboarding_date
                })
                .ToListAsync();
            return new PagedResult<AdminSocietyDto>(items, total, page, pageSize);
        }

        public async Task<AdminSocietyDto?> GetSocietyByIdAsync(long id)
        {
            var s = await _db.societies.AsNoTracking().FirstOrDefaultAsync(x => x.id == id);
            if (s == null) return null;

            // Single aggregated query instead of 4 separate COUNTs
            var counts = await _db.flats
                .Where(f => f.society_id == id && !f.is_deleted)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    FlatCount       = g.Count(),
                    ActiveFlatCount = g.Count(f => f.status_id != null)
                })
                .FirstOrDefaultAsync();

            var userCounts = await _db.users
                .Where(u => u.society_id == id && !u.is_deleted)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    UserCount       = g.Count(),
                    ActiveUserCount = g.Count(u => u.is_active)
                })
                .FirstOrDefaultAsync();

            var activeSub = await _db.subscriptions
                .AsNoTracking()
                .Where(sub => sub.society_id == id
                           && (sub.status == "active" || sub.status == "trial"))
                .OrderByDescending(sub => sub.created_at)
                .Select(sub => new AdminSocietySubscriptionSummary
                {
                    Id               = sub.id,
                    PlanName         = sub.plan.name,
                    Status           = sub.status,
                    SubscribedAmount = sub.subscribed_amount,
                    Currency         = sub.currency,
                    CurrentPeriodEnd = sub.current_period_end,
                    TrialEnd         = sub.trial_end
                })
                .FirstOrDefaultAsync();

            return new AdminSocietyDto
            {
                Id                      = s.id,
                PublicId                = s.public_id,
                Name                    = s.name,
                Address                 = s.address,
                City                    = s.city,
                State                   = s.state,
                Pincode                 = s.pincode,
                Currency                = s.currency,
                DefaultMaintenanceCycle = s.default_maintenance_cycle,
                CreatedAt               = s.created_at,
                UpdatedAt               = s.updated_at,
                IsDeleted               = s.is_deleted,
                DeletedAt               = s.deleted_at,
                OnboardingDate          = s.onboarding_date,
                FlatCount               = counts?.FlatCount ?? 0,
                ActiveFlatCount         = counts?.ActiveFlatCount ?? 0,
                UserCount               = userCounts?.UserCount ?? 0,
                ActiveUserCount         = userCounts?.ActiveUserCount ?? 0,
                ActiveSubscription      = activeSub
            };
        }
    }
}
