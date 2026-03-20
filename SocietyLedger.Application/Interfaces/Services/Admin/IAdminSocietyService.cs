using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Shared;

namespace SocietyLedger.Application.Interfaces.Services.Admin
{
    public interface IAdminSocietyService
    {
        Task<PagedResult<AdminSocietyDto>> GetSocietiesAsync(int page, int pageSize, string? search = null);
        Task<AdminSocietyDto?> GetSocietyByIdAsync(long id);
        Task<AdminSocietyDto> UpdateSocietyAsync(long id, AdminSocietyUpdateRequest request);
    }
}
