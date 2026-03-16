using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Shared;

namespace SocietyLedger.Application.Interfaces.Services.Admin
{
    public interface IAdminFeatureFlagService
    {
        Task<PagedResult<FeatureFlagDto>> GetFlagsAsync(int page, int pageSize, string? search = null, long? societyId = null);
        Task<FeatureFlagDto?> GetFlagByIdAsync(long id);
        Task<FeatureFlagDto> CreateFlagAsync(FeatureFlagCreateRequest request);
        Task<FeatureFlagDto> UpdateFlagAsync(long id, FeatureFlagUpdateRequest request);
        Task DeleteFlagAsync(long id);
    }
}
