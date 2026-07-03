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
        private const int MaxPageSize = 200;
        private readonly AppDbContext _db;

        public AdminPlanService(AppDbContext db) { _db = db; }

        public async Task<PagedResult<AdminPlanDto>> GetPlansAsync(int page, int pageSize, string? search = null, bool? isActive = null)
        {
            pageSize = Math.Min(pageSize, MaxPageSize);
            var query = _db.plans.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(p => EF.Functions.ILike(p.name, $"%{search}%"));
            if (isActive.HasValue)
                query = query.Where(p => p.is_active == isActive);

            var total = await query.CountAsync();
            var items = await query
                .OrderBy(p => p.display_order)
                .ThenByDescending(p => p.created_at)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<AdminPlanDto>(items.Select(MapToDto).ToList(), total, page, pageSize);
        }

        public async Task<AdminPlanDto?> GetPlanByIdAsync(Guid id)
        {
            var p = await _db.plans.AsNoTracking().FirstOrDefaultAsync(x => x.id == id);
            return p == null ? null : MapToDto(p);
        }

        public async Task<AdminPlanDto> CreatePlanAsync(AdminPlanCreateRequest request)
        {
            if (await _db.plans.AnyAsync(x => x.name == request.Name))
                throw new ConflictException($"Plan with name '{request.Name}' already exists.");

            var plan = new plan
            {
                id = Guid.NewGuid(),
                name = request.Name,
                description = request.Description,
                price = request.Price,
                monthly_amount = request.MonthlyAmount,
                currency = request.Currency,
                is_active = request.IsActive,
                is_popular = request.IsPopular,
                plan_group = request.PlanGroup,
                display_order = request.DisplayOrder,
                max_flats = request.MaxFlats ?? 0,
                discount_percentage = (int?)request.DiscountPercentage,
                duration_months = request.DurationMonths,
                created_at = DateTime.UtcNow,
                updated_at = DateTime.UtcNow
            };

            _db.plans.Add(plan);
            await _db.SaveChangesAsync();
            return MapToDto(plan);
        }

        public async Task<AdminPlanDto> UpdatePlanAsync(Guid id, AdminPlanUpdateRequest request)
        {
            var plan = await _db.plans.FirstOrDefaultAsync(x => x.id == id);
            if (plan == null) throw new NotFoundException("Plan", id.ToString());

            if (request.Name != null) plan.name = request.Name;
            if (request.Description != null) plan.description = request.Description;
            if (request.Price.HasValue) plan.price = request.Price.Value;
            if (request.MonthlyAmount.HasValue) plan.monthly_amount = request.MonthlyAmount.Value;
            if (request.Currency != null) plan.currency = request.Currency;
            if (request.IsActive.HasValue) plan.is_active = request.IsActive.Value;
            if (request.IsPopular.HasValue) plan.is_popular = request.IsPopular.Value;
            if (request.PlanGroup != null) plan.plan_group = request.PlanGroup;
            if (request.DisplayOrder.HasValue) plan.display_order = request.DisplayOrder.Value;
            if (request.MaxFlats.HasValue) plan.max_flats = request.MaxFlats.Value;
            if (request.DiscountPercentage.HasValue) plan.discount_percentage = (int?)request.DiscountPercentage.Value;
            if (request.DurationMonths.HasValue) plan.duration_months = request.DurationMonths.Value;
            plan.updated_at = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return MapToDto(plan);
        }

        public async Task DeletePlanAsync(Guid id)
        {
            var plan = await _db.plans.FirstOrDefaultAsync(x => x.id == id);
            if (plan == null) throw new NotFoundException("Plan", id.ToString());
            _db.plans.Remove(plan);
            await _db.SaveChangesAsync();
        }

        private static AdminPlanDto MapToDto(plan p) => new()
        {
            Id = p.id,
            Name = p.name,
            Description = p.description,
            Price = p.price,
            MonthlyAmount = p.monthly_amount,
            Currency = p.currency,
            IsActive = p.is_active,
            IsPopular = p.is_popular,
            PlanGroup = p.plan_group,
            DisplayOrder = p.display_order,
            MaxFlats = p.max_flats > 0 ? p.max_flats : (int?)null,
            DiscountPercentage = p.discount_percentage,
            DurationMonths = p.duration_months,
            CreatedAt = p.created_at,
            UpdatedAt = p.updated_at
        };
    }
}
