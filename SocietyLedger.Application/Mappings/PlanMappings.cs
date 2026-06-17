using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Application.DTOs.Plan;
using SocietyLedger.Domain.Entities;

namespace SocietyLedger.Application.Mappings
{
    public static class PlanMappings
    {
        public static PlanResponse ToResponse(this Plan plan) => new()
        {
            Id = plan.Id,
            Name = plan.Name,
            Price = plan.Price,
            MonthlyAmount = plan.Price,
            Currency = plan.Currency,
            IsActive = plan.IsActive,
            CreatedAt = plan.CreatedAt,
            UpdatedAt = plan.UpdatedAt,
            DurationMonths = plan.DurationMonths,
            MaxFlats = plan.MaxFlats,
            PlanGroup = plan.PlanGroup,
            DiscountPercentage = plan.DiscountPercentage,
            DisplayOrder = plan.DisplayOrder,
            IsPopular = plan.IsPopular,
            Description = plan.Description
        };

        public static AdminPlanDto ToAdminDto(this Plan plan) => new()
        {
            Id = plan.Id,
            Name = plan.Name,
            Price = plan.Price,
            MonthlyAmount = plan.Price,
            Currency = plan.Currency,
            IsActive = plan.IsActive,
            CreatedAt = plan.CreatedAt,
            UpdatedAt = plan.UpdatedAt,
            DurationMonths = plan.DurationMonths,
            MaxFlats = plan.MaxFlats,
            PlanGroup = plan.PlanGroup,
            DiscountPercentage = plan.DiscountPercentage,
            DisplayOrder = plan.DisplayOrder,
            IsPopular = plan.IsPopular,
            Description = plan.Description
        };
    }
}
