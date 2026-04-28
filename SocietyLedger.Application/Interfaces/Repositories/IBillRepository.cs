namespace SocietyLedger.Application.Interfaces.Repositories
{
    public interface IBillRepository
    {
        /// <summary>Returns true if any non-deleted bill exists for the given society and period.</summary>
        Task<bool> ExistsForPeriodAsync(long societyId, string period);

        /// <summary>Returns the count of non-deleted bills for the given society and period.</summary>
        Task<int> CountForPeriodAsync(long societyId, string period);

        /// <summary>Returns true if a non-deleted bill exists for the given flat and period.</summary>
        Task<bool> ExistsForFlatAndPeriodAsync(long flatId, string period);

        /// <summary>Returns true if the flat has any unpaid (non-paid, non-cancelled) bills.</summary>
        Task<bool> HasUnpaidBillsAsync(long flatId);

        /// <summary>
        /// Returns unpaid bill amounts for a flat (for outstanding calculation on delete).
        /// </summary>
        Task<IEnumerable<(decimal Amount, decimal PaidAmount)>> GetUnpaidBillAmountsAsync(long flatId);

        /// <summary>Returns true if the flat has any unpaid bills (excluding paid and cancelled).</summary>
        Task<bool> HasUnpaidBillsExcludingStatusAsync(long flatId, string excludeStatus1, string excludeStatus2);

        /// <summary>Adds a range of bill entities and saves.</summary>
        Task AddRangeAsync(IEnumerable<BillAddDto> bills);

        /// <summary>Adds a single bill entity and saves.</summary>
        Task AddAsync(BillAddDto bill);

        /// <summary>Persists pending changes.</summary>
        Task SaveChangesAsync();
    }

    public record BillAddDto(
        long SocietyId,
        long FlatId,
        string Period,
        decimal Amount,
        string StatusCode,
        long? GeneratedBy,
        DateTime GeneratedAt,
        DateTime CreatedAt,
        string Source
    );
}
