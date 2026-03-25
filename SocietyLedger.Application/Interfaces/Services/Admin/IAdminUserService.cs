using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Shared;

namespace SocietyLedger.Application.Interfaces.Services.Admin
{
    public interface IAdminUserService
    {
        Task<PagedResult<AdminUserDto>> GetUsersAsync(int page, int pageSize, long? societyId = null, string? search = null, bool? isActive = null, bool? isDeleted = null);
        Task<AdminUserDto?> GetUserByIdAsync(long id);
    }
}
