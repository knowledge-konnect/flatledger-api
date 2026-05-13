using System.Text.RegularExpressions;

namespace SocietyLedger.Domain.Constants
{
    /// <summary>
    /// Centralised validation patterns — single source of truth.
    /// Fix #22: period regex was duplicated across endpoint and service; now defined once here.
    /// </summary>
    public static class ValidationPatterns
    {
        public const string BillingPeriodPattern = @"^\d{4}-\d{2}$";

        /// <summary>Compiled regex for billing period format (YYYY-MM).</summary>
        public static readonly Regex BillingPeriod =
            new(BillingPeriodPattern, RegexOptions.Compiled);

        /// <summary>Maximum number of flats allowed in a single bulk-create request.</summary>
        public const int MaxBulkFlats = 500;
    }
}
