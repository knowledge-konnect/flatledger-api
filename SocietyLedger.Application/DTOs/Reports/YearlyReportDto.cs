using Newtonsoft.Json;

namespace SocietyLedger.Application.DTOs.Reports
{
    public class YearlyReportDto
    {
        [JsonProperty("society_name")]
        public string SocietyName { get; set; } = string.Empty;

        [JsonProperty("year_label")]
        public string YearLabel { get; set; } = string.Empty;

        [JsonProperty("summary")]
        public string? Summary { get; set; }

        [JsonProperty("alerts")]
        public List<string> Alerts { get; set; } = new();

        [JsonProperty("fund_position")]
        public YearFundPositionDto FundPosition { get; set; } = new();

        [JsonProperty("month_summary")]
        public List<MonthSummaryDto> MonthSummary { get; set; } = new();

        [JsonProperty("expenses")]
        public List<ExpenseDto> Expenses { get; set; } = new();
    }

    public class YearFundPositionDto
    {
        [JsonProperty("opening_balance")]
        public decimal OpeningBalance { get; set; }

        private decimal _totalBilled;
        private decimal _totalCollected;
        private decimal _totalExpenses;

        [JsonProperty("total_billed")]
        public decimal TotalBilled { get => _totalBilled; set => _totalBilled = value; }
        [JsonProperty("billed")]
        private decimal BilledSetter { set => _totalBilled = value; }

        [JsonProperty("total_collected")]
        public decimal TotalCollected { get => _totalCollected; set => _totalCollected = value; }
        [JsonProperty("collected")]
        private decimal CollectedSetter { set => _totalCollected = value; }

        [JsonProperty("total_expenses")]
        public decimal TotalExpenses { get => _totalExpenses; set => _totalExpenses = value; }
        [JsonProperty("expenses")]
        private decimal ExpensesSetter { set => _totalExpenses = value; }

        [JsonProperty("closing_balance")]
        public decimal ClosingBalance { get; set; }
    }

    public class MonthSummaryDto
    {
        [JsonProperty("month_start")]
        public string? MonthStart { get; set; }

        [JsonProperty("month_label")]
        public string MonthLabel { get; set; } = string.Empty;

        [JsonProperty("billed")]
        public decimal Billed { get; set; }

        [JsonProperty("collected")]
        public decimal Collected { get; set; }

        [JsonProperty("expenses")]
        public decimal Expenses { get; set; }

        [JsonProperty("net")]
        public decimal Net { get; set; }

        // Accept both "month_status" and legacy "status" keys from DB JSON
        [JsonProperty("month_status")]
        public string? MonthStatus { get; set; }
        [JsonProperty("status")]
        private string? StatusSetter { set => MonthStatus = value; }
    }
}
