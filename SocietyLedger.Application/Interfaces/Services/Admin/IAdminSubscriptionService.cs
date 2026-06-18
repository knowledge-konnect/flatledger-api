using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Shared;

namespace SocietyLedger.Application.Interfaces.Services.Admin
{
    public interface IAdminSubscriptionService
    {
        /// <param name="societyId">Filter by society (preferred). Overrides userId if both are provided.</param>
        /// <param name="userId">Legacy filter by user_id. Prefer societyId for new callers.</param>
        Task<PagedResult<AdminSubscriptionDto>> GetSubscriptionsAsync(int page, int pageSize, string? status = null, long? userId = null, long? societyId = null);
        Task<AdminSubscriptionDto?> GetSubscriptionByIdAsync(Guid id);
    }
}
