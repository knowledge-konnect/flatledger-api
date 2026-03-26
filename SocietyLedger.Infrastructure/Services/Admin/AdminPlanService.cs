using Microsoft.EntityFrameworkCore;
using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Application.Interfaces.Services.Admin;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Persistence.Entities;
using SocietyLedger.Shared;

namespace SocietyLedger.Infrastructure.Services.Admin
{
    public class AdminPlanService : IAdminPlanService
    {
        private readonly AppDbContext _db;
        public AdminPlanService(AppDbContext db) { _db = db; }

        public async Task<PagedResult<AdminPlanDto>> GetPlansAsync(int page, int pageSize, string? search = null, bool? isActive = null)
        {
            var query = _db.plans.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(p => p.name.ToLower().Contains(search.ToLower()));
            if (isActive.HasValue)
                query = query.Where(p => p.is_active == isActive);
            var total = await query.CountAsync();
            var items = await query.OrderByDescending(p => p.created_at)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new AdminPlanDto
                {
                    Id = p.id,
                    Name = p.name,
                    MonthlyAmount = p.monthly_amount,
                    Currency = p.currency,
                    IsActive = p.is_active,
                    CreatedAt = p.created_at,
                    DurationMonths = p.duration_months
                })
                .ToListAsync();
            return new PagedResult<AdminPlanDto>(items, total, page, pageSize);
        }

        public async Task<AdminPlanDto?> GetPlanByIdAsync(Guid id)
        {
            var p = await _db.plans.AsNoTracking().FirstOrDefaultAsync(x => x.id == id);
            if (p == null) return null;
            return new AdminPlanDto
            {
                Id = p.id,
                Name = p.name,
                MonthlyAmount = p.monthly_amount,
                Currency = p.currency,
                IsActive = p.is_active,
                CreatedAt = p.created_at,
                DurationMonths = p.duration_months
            };
        }

        public async Task<AdminPlanDto> CreatePlanAsync(AdminPlanCreateRequest request)
        {
            if (await _db.plans.AnyAsync(x => x.name == request.Name))
                throw new ConflictException($"Plan with name '{request.Name}' already exists.");
            var plan = new plan
            {
                id = Guid.NewGuid(),
                name = request.Name,
                monthly_amount = request.MonthlyAmount,
                currency = request.Currency,
                is_active = true,
                created_at = DateTime.UtcNow,
                duration_months = request.DurationMonths
            };
            _db.plans.Add(plan);
            await _db.SaveChangesAsync();
            return await GetPlanByIdAsync(plan.id) ?? throw new Exception("Failed to create plan");
        }

        public async Task<AdminPlanDto> UpdatePlanAsync(Guid id, AdminPlanUpdateRequest request)
        {
            var plan = await _db.plans.FirstOrDefaultAsync(x => x.id == id);
            if (plan == null) throw new NotFoundException("Plan", id.ToString());
            plan.name = request.Name;
            plan.monthly_amount = request.MonthlyAmount;
            plan.currency = request.Currency;
            plan.is_active = request.IsActive;
            plan.duration_months = request.DurationMonths;
            await _db.SaveChangesAsync();
            return await GetPlanByIdAsync(plan.id) ?? throw new Exception("Failed to update plan");
        }

        public async Task DeletePlanAsync(Guid id)
        {
            var plan = await _db.plans.FirstOrDefaultAsync(x => x.id == id);
            if (plan == null) throw new NotFoundException("Plan", id.ToString());
            _db.plans.Remove(plan);
            await _db.SaveChangesAsync();
        }
    }
}
