using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Shared;

namespace SocietyLedger.Application.Interfaces.Services.Admin
{
    public interface IAdminBillService
    {
        Task<PagedResult<AdminBillDto>> GetBillsAsync(int page, int pageSize, long? societyId = null, string? status = null, string? period = null, DateTime? from = null, DateTime? to = null);
    }
}
