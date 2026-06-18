using SocietyLedger.Application.DTOs.Plan;

namespace SocietyLedger.Application.Interfaces.Services
{
    public interface IPlanService
    {
        Task<IEnumerable<PlanResponse>> GetActivePlansAsync();
        /// <summary>Returns the plan by ID. Throws <see cref="SocietyLedger.Domain.Exceptions.NotFoundException"/> if not found.</summary>
        Task<PlanResponse> GetPlanByIdAsync(Guid id);
    }
}