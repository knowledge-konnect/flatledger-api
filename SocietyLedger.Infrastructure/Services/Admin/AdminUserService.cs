using Microsoft.EntityFrameworkCore;
using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Application.Interfaces.Services.Admin;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Shared;

namespace SocietyLedger.Infrastructure.Services.Admin
{
    public class AdminUserService : IAdminUserService
    {
        private readonly AppDbContext _db;
        public AdminUserService(AppDbContext db) { _db = db; }

        public async Task<PagedResult<AdminUserDto>> GetUsersAsync(int page, int pageSize, long? societyId = null, string? search = null, bool? isActive = null, bool? isDeleted = null)
        {
            var query = _db.users
                .AsNoTracking()
                .Join(_db.societies, u => u.society_id, s => s.id,
                      (u, s) => new { u, SocietyName = s.name });

            if (societyId.HasValue)
                query = query.Where(x => x.u.society_id == societyId);
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(x => x.u.name.ToLower().Contains(search.ToLower())
                                      || (x.u.email != null && x.u.email.ToLower().Contains(search.ToLower()))
                                      || (x.u.mobile != null && x.u.mobile.Contains(search)));
            if (isActive.HasValue)
                query = query.Where(x => x.u.is_active == isActive.Value);
            if (isDeleted.HasValue)
                query = query.Where(x => x.u.is_deleted == isDeleted.Value);
            else
                query = query.Where(x => !x.u.is_deleted);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(x => x.u.created_at)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new AdminUserDto
                {
                    Id = x.u.id,
                    PublicId = x.u.public_id,
                    SocietyId = x.u.society_id,
                    SocietyName = x.SocietyName,
                    Name = x.u.name,
                    Email = x.u.email,
                    Mobile = x.u.mobile,
                    Username = x.u.username,
                    RoleId = x.u.role_id,
                    IsActive = x.u.is_active,
                    IsDeleted = x.u.is_deleted,
                    LastLogin = x.u.last_login,
                    CreatedAt = x.u.created_at,
                    SubscriptionStatus = x.u.subscription_status,
                    // subscription_plan is deprecated — use subscriptions table for plan data
                    SubscriptionPlan = null,
                    TrialEndsDate = x.u.trial_ends_date,
                    NextBillingDate = x.u.next_billing_date
                })
                .ToListAsync();

            return new PagedResult<AdminUserDto>(items, total, page, pageSize);
        }

        public async Task<AdminUserDto?> GetUserByIdAsync(long id)
        {
            return await _db.users
                .AsNoTracking()
                .Where(u => u.id == id)
                .Join(_db.societies, u => u.society_id, s => s.id,
                      (u, s) => new AdminUserDto
                      {
                          Id = u.id,
                          PublicId = u.public_id,
                          SocietyId = u.society_id,
                          SocietyName = s.name,
                          Name = u.name,
                          Email = u.email,
                          Mobile = u.mobile,
                          Username = u.username,
                          RoleId = u.role_id,
                          IsActive = u.is_active,
                          IsDeleted = u.is_deleted,
                          LastLogin = u.last_login,
                          CreatedAt = u.created_at,
                          SubscriptionStatus = u.subscription_status,
                          // subscription_plan is deprecated — use subscriptions table for plan data
                          SubscriptionPlan = null,
                          TrialEndsDate = u.trial_ends_date,
                          NextBillingDate = u.next_billing_date
                      })
                .FirstOrDefaultAsync();
        }
    }
}
