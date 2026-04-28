using Newtonsoft.Json;

namespace SocietyLedger.Application.DTOs.Flat
{
    public class FlatFinancialSummaryResponse
    {
        /// <summary>
        /// Remaining amount from pre-migration dues (Opening Balance).
        /// This is based on adjustments.remaining_amount.
        /// </summary>
        public decimal OpeningBalanceRemaining { get; set; }

        /// <summary>
        /// Total unpaid balance from generated bills.
        /// Calculated as SUM(bills.amount - bills.paid_amount).
        /// </summary>
        public decimal BillOutstanding { get; set; }

        /// <summary>
        /// Combined outstanding (signed):
        /// OpeningBalanceRemaining + BillOutstanding.
        /// Positive = member owes the society; Negative = society owes the member (advance).
        /// </summary>
        public decimal TotalOutstanding { get; set; }

        /// <summary>
        /// Total amount charged through bills (historical).
        /// Used only for UI display or breakdown.
        /// </summary>
        public decimal TotalCharges { get; set; }

        /// <summary>
        /// Total payments made by the member.
        /// Used for display only.
        /// </summary>
        public decimal TotalPayments { get; set; }

        [JsonProperty("balance_sign_legend")]
        public string BalanceSignLegend => "Positive = member owes the society; Negative = society owes member (advance).";
    }
}
