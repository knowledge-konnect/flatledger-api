namespace SocietyLedger.Application.Interfaces.Repositories
{
    using SocietyLedger.Application.DTOs.MaintenancePayment;

    public interface IMaintenancePaymentRepository
    {
        Task<MaintenancePaymentEntity?> GetByPublicIdAsync(Guid publicId, long societyId);
        Task<IEnumerable<MaintenancePaymentEntity>> GetBySocietyIdAsync(long societyId, string? period = null, int page = 1, int pageSize = 50);
        Task<IEnumerable<MaintenancePaymentEntity>> GetByFlatPublicIdAsync(Guid flatPublicId, long societyId);
        Task<MaintenancePaymentEntity> CreateAsync(MaintenancePaymentEntity payment);
        Task UpdateByPublicIdAsync(Guid publicId, long societyId, Action<MaintenancePaymentEntity> updateAction);
        Task DeleteByPublicIdAsync(Guid publicId, long societyId);

        /// <summary>Returns payment rows for a flat suitable for rendering the flat ledger.</summary>
        Task<IReadOnlyList<PaymentLedgerEntry>> GetByFlatIdForLedgerAsync(long flatId, DateTime? startDate, DateTime? endDate);

        /// <summary>Returns the SUM of payment amounts for a flat with payment_date &lt; <paramref name="before"/>.</summary>
        Task<decimal> GetTotalAmountBeforeDateAsync(long flatId, DateTime before);

        /// <summary>Returns the SUM of all payment amounts for a flat (for financial summary).</summary>
        Task<decimal> GetTotalPaidByFlatIdAsync(long flatId);
    }

    /// <summary>Flat ledger projection for payment rows.</summary>
    public record PaymentLedgerEntry(
        DateTime PaymentDate,
        decimal Amount,
        string? Notes,
        string? ReferenceNumber);
}
