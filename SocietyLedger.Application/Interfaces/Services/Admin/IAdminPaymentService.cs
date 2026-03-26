using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Shared;

namespace SocietyLedger.Application.Interfaces.Services.Admin
{
    public interface IAdminPaymentService
    {
        Task<PagedResult<AdminPaymentDto>> GetPaymentsAsync(int page, int pageSize, long? societyId = null, string? paymentType = null, DateTime? from = null, DateTime? to = null);
    }
}
