using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Shared;

namespace SocietyLedger.Application.Interfaces.Services.Admin
{
    public interface IAdminSubscriptionService
    {
        Task<PagedResult<AdminSubscriptionDto>> GetSubscriptionsAsync(int page, int pageSize, string? status = null, long? userId = null);
        Task<AdminSubscriptionDto?> GetSubscriptionByIdAsync(Guid id);
    }
}
