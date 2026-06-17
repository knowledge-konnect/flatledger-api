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
            var items = await query.OrderBy(p => p.display_order).ThenByDescending(p => p.created_at)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => MapToDto(p))
                .ToListAsync();
            return new PagedResult<AdminPlanDto>(items, total, page, pageSize);
        }

        public async Task<AdminPlanDto?> GetPlanByIdAsync(Guid id)
        {
            var p = await _db.plans.AsNoTracking().FirstOrDefaultAsync(x => x.id == id);
            if (p == null) return null;
            return MapToDto(p);
        }

        public async Task<AdminPlanDto> CreatePlanAsync(AdminPlanCreateRequest request)
        {
            if (await _db.plans.AnyAsync(x => x.name == request.Name))
                throw new ConflictException($"Plan with name '{request.Name}' already exists.");

            var now = DateTime.UtcNow;
            var plan = new plan
            {
                id = Guid.NewGuid(),
                name = request.Name,
                price = request.MonthlyAmount,
                monthly_amount = request.MonthlyAmount,
                currency = request.Currency,
                is_active = true,
                created_at = now,
                updated_at = now,
                duration_months = request.DurationMonths,
                max_flats = request.MaxFlats,
                plan_group = request.PlanGroup,
                discount_percentage = request.DiscountPercentage,
                display_order = request.DisplayOrder,
                is_popular = request.IsPopular,
                description = request.Description
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
            plan.price = request.MonthlyAmount;
            plan.monthly_amount = request.MonthlyAmount;
            plan.currency = request.Currency;
            plan.is_active = request.IsActive;
            plan.duration_months = request.DurationMonths;
            plan.max_flats = request.MaxFlats;
            plan.plan_group = request.PlanGroup;
            plan.discount_percentage = request.DiscountPercentage;
            plan.display_order = request.DisplayOrder;
            plan.is_popular = request.IsPopular;
            plan.description = request.Description;
            plan.updated_at = DateTime.UtcNow;
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

        private static AdminPlanDto MapToDto(plan p)
        {
            var price = p.price > 0 ? p.price : p.monthly_amount;
            return new AdminPlanDto
            {
                Id = p.id,
                Name = p.name,
                Price = price,
                MonthlyAmount = price,
                Currency = p.currency,
                IsActive = p.is_active,
                CreatedAt = p.created_at,
                UpdatedAt = p.updated_at,
                DurationMonths = p.duration_months,
                MaxFlats = p.max_flats,
                PlanGroup = p.plan_group,
                DiscountPercentage = p.discount_percentage,
                DisplayOrder = p.display_order,
                IsPopular = p.is_popular,
                Description = p.description
            };
        }
    }
}
