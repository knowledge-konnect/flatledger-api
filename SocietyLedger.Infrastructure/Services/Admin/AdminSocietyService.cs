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
        private readonly AppDbContext _db;
        public AdminSocietyService(AppDbContext db) { _db = db; }

        public async Task<PagedResult<AdminSocietyDto>> GetSocietiesAsync(int page, int pageSize, string? search = null)
        {
            var query = _db.societies.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(s => s.name.ToLower().Contains(search.ToLower()));
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

            var flatCount       = await _db.flats.CountAsync(f => f.society_id == id && !f.is_deleted);
            var activeFlatCount = await _db.flats.CountAsync(f => f.society_id == id && !f.is_deleted && f.status_id != null);
            var userCount       = await _db.users.CountAsync(u => u.society_id == id && !u.is_deleted);
            var activeUserCount = await _db.users.CountAsync(u => u.society_id == id && !u.is_deleted && u.is_active);

            var activeSub = await _db.subscriptions
                .AsNoTracking()
                .Where(sub => _db.users.Any(u => u.society_id == id && u.id == sub.user_id)
                           && (sub.status == "active" || sub.status == "trial"))
                .OrderByDescending(sub => sub.created_at)
                .Select(sub => new AdminSocietySubscriptionSummary
                {
                    Id = sub.id,
                    PlanName = sub.plan.name,
                    Status = sub.status,
                    SubscribedAmount = sub.subscribed_amount,
                    Currency = sub.currency,
                    CurrentPeriodEnd = sub.current_period_end,
                    TrialEnd = sub.trial_end
                })
                .FirstOrDefaultAsync();

            return new AdminSocietyDto
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
                OnboardingDate = s.onboarding_date,
                FlatCount = flatCount,
                ActiveFlatCount = activeFlatCount,
                UserCount = userCount,
                ActiveUserCount = activeUserCount,
                ActiveSubscription = activeSub
            };
        }

    }
}
