namespace SocietyLedger.Application.Interfaces.Repositories
{
    public interface IAdjustmentRepository
    {
        /// <summary>
        /// Returns adjustment rows for a flat suitable for rendering the flat ledger.
        /// Optional date range filters are applied on <c>created_at</c>.
        /// </summary>
        Task<IReadOnlyList<AdjustmentLedgerEntry>> GetByFlatIdAsync(long flatId, DateTime? startDate, DateTime? endDate);

        /// <summary>
        /// Returns the SUM of adjustment amounts for a flat that were created before <paramref name="before"/>.
        /// Used to calculate the opening balance when a date-range filter is active on the ledger.
        /// </summary>
        Task<decimal> GetTotalAmountBeforeDateAsync(long flatId, DateTime before);

        /// <summary>
        /// Returns the remaining (uncollected) opening-balance amount for a flat.
        /// </summary>
        Task<decimal> GetOpeningBalanceRemainingAsync(long flatId);
    }

    /// <summary>Flat ledger projection for adjustment rows.</summary>
    public record AdjustmentLedgerEntry(
        DateTime CreatedAt,
        string EntryType,
        string? Reason,
        decimal Amount);
}
