namespace SocietyLedger.Infrastructure.Services.Common
{
    /// <summary>
    /// Dapper-only projection types used exclusively by <see cref="MaintenancePaymentService"/>.
    /// These are NOT EF-tracked entities — they exist solely to receive Dapper query results.
    /// </summary>

    /// <summary>Flat row returned by <see cref="SqlQueries.LockFlatByPublicId"/>.</summary>
    internal sealed record FlatRow(long id, Guid public_id, string flat_no, long society_id);

    /// <summary>
    /// Opening-balance adjustment row returned by <see cref="SqlQueries.LockOpeningBalanceAdjustments"/>.
    /// Uses <c>set</c> setters so Dapper can populate properties via its standard reflection path.
    /// </summary>
    internal sealed class AdjustmentRow
    {
        public long    id               { get; set; }
        public Guid    public_id        { get; set; }
        public decimal remaining_amount { get; set; }
    }

    /// <summary>
    /// Bill row returned by <see cref="SqlQueries.LockUnpaidBillsByFlat"/>.
    /// <c>PaidAmount</c> uses PascalCase because Dapper's underscore-stripping maps
    /// the SQL alias <c>paid_amount</c> → <c>PaidAmount</c> automatically.
    /// All setters use <c>set</c> for standard Dapper compatibility.
    /// </summary>
    internal sealed class BillRow
    {
        public long    id          { get; set; }
        public Guid    public_id   { get; set; }
        public decimal amount      { get; set; }
        public decimal PaidAmount  { get; set; }
        public string  status_code { get; set; } = string.Empty;
        public string  period      { get; set; } = string.Empty;
    }

    /// <summary>
    /// CTE result row returned by <see cref="SqlQueries.MaintenanceSummary"/>.
    /// Double-quoted SQL aliases (<c>AS "TotalCharges"</c>) ensure exact PascalCase mapping.
    /// </summary>
    internal sealed class SummaryRow
    {
        public decimal TotalCharges    { get; set; }
        public decimal TotalCollected  { get; set; }
        public decimal BillOutstanding { get; set; }
        public decimal ObRemaining     { get; set; }
    }
}
