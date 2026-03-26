using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Shared;

namespace SocietyLedger.Application.Interfaces.Services.Admin
{
    public interface IAdminPlatformSettingService
    {
        Task<PagedResult<PlatformSettingDto>> GetSettingsAsync(int page, int pageSize, string? search = null);
        Task<PlatformSettingDto?> GetSettingByKeyAsync(string key);
        Task<PlatformSettingDto> UpsertSettingAsync(PlatformSettingUpsertRequest request);
        Task DeleteSettingAsync(string key);
    }
}
