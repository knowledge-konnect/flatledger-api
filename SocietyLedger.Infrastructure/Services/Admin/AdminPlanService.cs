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
            var items = await query.OrderByDescending(p => p.created_at)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new AdminPlanDto
                {
                    Id = p.id,
                    Name = p.name,
                    Price = p.price,
                    Currency = p.currency,
                    IsActive = p.is_active,
                    CreatedAt = p.created_at,
                    DurationMonths = p.duration_months,
                    MaxFlats = p.max_flats,
                    PlanGroup = p.plan_group,
                    IsPopular = p.is_popular,
                    Description = p.description,
                    DiscountPercentage = p.discount_percentage,
                    DisplayOrder = p.display_order
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
                Price = p.price,
                Currency = p.currency,
                IsActive = p.is_active,
                CreatedAt = p.created_at,
                DurationMonths = p.duration_months,
                MaxFlats = p.max_flats,
                PlanGroup = p.plan_group,
                IsPopular = p.is_popular,
                Description = p.description,
                DiscountPercentage = p.discount_percentage,
                DisplayOrder = p.display_order
            };
        }

        public async Task<AdminPlanDto> CreatePlanAsync(AdminPlanCreateRequest request)
        {
            if (await _db.plans.AnyAsync(x => x.plan_group == request.PlanGroup && x.duration_months == request.DurationMonths))
                throw new ConflictException($"Plan with group '{request.PlanGroup}' and duration {request.DurationMonths} already exists.");
            var plan = new plan
            {
                id = Guid.NewGuid(),
                name = request.Name,
                price = request.Price,
                currency = request.Currency,
                is_active = true,
                created_at = DateTime.UtcNow,
                duration_months = request.DurationMonths,
                max_flats = request.MaxFlats,
                plan_group = request.PlanGroup,
                is_popular = request.IsPopular,
                description = request.Description,
                discount_percentage = request.DiscountPercentage,
                display_order = request.DisplayOrder
            };
            _db.plans.Add(plan);
            await _db.SaveChangesAsync();
            return await GetPlanByIdAsync(plan.id) ?? throw new AppException("Failed to retrieve plan after creation.");
        }

        public async Task<AdminPlanDto> UpdatePlanAsync(Guid id, AdminPlanUpdateRequest request)
        {
            var plan = await _db.plans.FirstOrDefaultAsync(x => x.id == id);
            if (plan == null) throw new NotFoundException("Plan", id.ToString());
            plan.name = request.Name;
            plan.price = request.Price;
            plan.currency = request.Currency;
            plan.is_active = request.IsActive;
            plan.duration_months = request.DurationMonths;
            plan.max_flats = request.MaxFlats;
            plan.plan_group = request.PlanGroup;
            plan.is_popular = request.IsPopular;
            plan.description = request.Description;
            plan.discount_percentage = request.DiscountPercentage;
            plan.display_order = request.DisplayOrder;
            await _db.SaveChangesAsync();
            return await GetPlanByIdAsync(plan.id) ?? throw new AppException("Failed to retrieve plan after update.");
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
