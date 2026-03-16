using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Shared;

namespace SocietyLedger.Application.Interfaces.Services.Admin
{
    public interface IAdminPlanService
    {
        Task<PagedResult<AdminPlanDto>> GetPlansAsync(int page, int pageSize, string? search = null, bool? isActive = null);
        Task<AdminPlanDto?> GetPlanByIdAsync(Guid id);
        Task<AdminPlanDto> CreatePlanAsync(AdminPlanCreateRequest request);
        Task<AdminPlanDto> UpdatePlanAsync(Guid id, AdminPlanUpdateRequest request);
        Task DeletePlanAsync(Guid id);
    }
}
