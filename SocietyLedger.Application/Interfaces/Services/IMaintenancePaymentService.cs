using SocietyLedger.Application.DTOs.MaintenancePayment;

namespace SocietyLedger.Application.Interfaces.Services
{
    public interface IMaintenancePaymentService
    {
        Task<MaintenancePaymentResponse> CreateMaintenancePaymentAsync(long userId, CreateMaintenancePaymentRequest request);
        Task<MaintenancePaymentResponse> AllocateMaintenancePaymentAsync(long userId, CreateMaintenancePaymentRequest request, string idempotencyKey);
        Task<MaintenancePaymentResponse> GetMaintenancePaymentAsync(Guid publicId, long userId);
        Task<IEnumerable<MaintenancePaymentResponse>> GetMaintenancePaymentsBySocietyAsync(long userId, string? period = null, int page = 1, int pageSize = 50);
        Task<IEnumerable<MaintenancePaymentResponse>> GetMaintenancePaymentsByFlatAsync(Guid flatPublicId, long userId);
        Task<MaintenancePaymentResponse> UpdateMaintenancePaymentAsync(Guid publicId, long userId, UpdateMaintenancePaymentRequest request);
        Task DeleteMaintenancePaymentAsync(Guid publicId, long userId);
        Task<IEnumerable<PaymentModeResponse>> GetPaymentModesAsync();
        Task<MaintenanceSummaryResponse> GetMaintenanceSummaryAsync(long userId, string period);
        Task<MaintenancePaymentResponse> ProcessPaymentAsync(MaintenancePaymentRequest request, long userId);
    }
}
