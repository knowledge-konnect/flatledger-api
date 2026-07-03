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
            MonthlyAmount = plan.MonthlyAmount,
            Currency = plan.Currency,
            IsActive = plan.IsActive,
            MaxFlats = plan.MaxFlats,
            DurationMonths = plan.DurationMonths,
            CreatedAt = plan.CreatedAt
        };

        public static AdminPlanDto ToAdminDto(this Plan plan) => new()
        {
            Id = plan.Id,
            Name = plan.Name,
            Price = plan.Price,
            MonthlyAmount = plan.MonthlyAmount,
            Currency = plan.Currency,
            IsActive = plan.IsActive,
            MaxFlats = plan.MaxFlats,
            DurationMonths = plan.DurationMonths,
            CreatedAt = plan.CreatedAt
        };
    }
}
