using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Shared;

namespace SocietyLedger.Application.Interfaces.Services.Admin
{
    public interface IAdminInvoiceService
    {
        Task<PagedResult<AdminInvoiceDto>> GetInvoicesAsync(int page, int pageSize, long? userId = null, string? status = null, string? invoiceType = null, DateTime? from = null, DateTime? to = null);
    }
}
