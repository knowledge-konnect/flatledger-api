using Newtonsoft.Json;

namespace SocietyLedger.Application.DTOs.Reports
{
    public class MonthlyReportDto
    {
        [JsonProperty("society_name")]
        public string SocietyName { get; set; } = string.Empty;

        [JsonProperty("period_label")]
        public string PeriodLabel { get; set; } = string.Empty;

        [JsonProperty("summary")]
        public string? Summary { get; set; }

        [JsonProperty("alerts")]
        public List<string> Alerts { get; set; } = new();

        [JsonProperty("payment_summary")]
        public PaymentSummaryDto PaymentSummary { get; set; } = new();

        [JsonProperty("fund_position")]
        public FundPositionDto FundPosition { get; set; } = new();

        [JsonProperty("flat_details")]
        public List<FlatDetailDto> FlatDetails { get; set; } = new();

        [JsonProperty("expenses")]
        public List<ExpenseDto> Expenses { get; set; } = new();
    }

    public class FundPositionDto
    {
        [JsonProperty("opening_balance")]
        public decimal OpeningBalance { get; set; }

        [JsonProperty("collected")]
        public decimal Collected { get; set; }

        [JsonProperty("expenses")]
        public decimal Expenses { get; set; }

        [JsonProperty("closing_balance")]
        public decimal ClosingBalance { get; set; }
    }

    public class PaymentSummaryDto
    {
        [JsonProperty("total_flats")]
        public int TotalFlats { get; set; }

        [JsonProperty("paid")]
        public int Paid { get; set; }

        [JsonProperty("pending")]
        public int Pending { get; set; }

        [JsonProperty("total_billed")]
        public decimal TotalBilled { get; set; }

        [JsonProperty("total_collected")]
        public decimal TotalCollected { get; set; }

        [JsonProperty("pending_amount")]
        public decimal PendingAmount { get; set; }

        [JsonProperty("collection_efficiency")]
        public decimal CollectionEfficiency { get; set; }
    }

    public class FlatDetailDto
    {
        [JsonProperty("flat_no")]
        public string FlatNo { get; set; } = string.Empty;

        [JsonProperty("owner_name")]
        public string? OwnerName { get; set; }

        [JsonProperty("billed_amount")]
        public decimal BilledAmount { get; set; }

        [JsonProperty("paid_amount")]
        public decimal PaidAmount { get; set; }

        [JsonProperty("balance_amount")]
        public decimal BalanceAmount { get; set; }

        [JsonProperty("status")]
        public string? Status { get; set; }
    }

    public class DefaulterDto
    {
        [JsonProperty("flat_no")]
        public string FlatNo { get; set; } = string.Empty;

        [JsonProperty("owner_name")]
        public string? OwnerName { get; set; }

        [JsonProperty("pending")]
        public decimal Pending { get; set; }
    }

    public class ExpenseDto
    {
        [JsonProperty("category_name")]
        public string CategoryName { get; set; } = string.Empty;

        [JsonProperty("total_amount")]
        public decimal TotalAmount { get; set; }
    }
}
