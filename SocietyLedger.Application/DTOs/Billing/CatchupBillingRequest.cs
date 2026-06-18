namespace SocietyLedger.Application.DTOs.Billing
{
    /// <summary>
    /// Request body for POST /billing/catchup.
    /// </summary>
    public record CatchupBillingRequest
    {
        /// <summary>
        /// Target billing period in yyyy-MM format (e.g. "2026-04").
        /// When omitted, defaults to the previous calendar month.
        /// </summary>
        public string? Period { get; init; }

        /// <summary>
        /// Resolves the period to a UTC DateTime (first day of the month).
        /// Falls back to the previous UTC month when Period is null/empty.
        /// </summary>
        public DateTime GetBillingMonthDate()
        {
            if (!string.IsNullOrWhiteSpace(Period) &&
                DateTime.TryParseExact(Period, "yyyy-MM",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var parsed))
            {
                return new DateTime(parsed.Year, parsed.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            }
            var prev = DateTime.UtcNow.AddMonths(-1);
            return new DateTime(prev.Year, prev.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        }
    }
}
