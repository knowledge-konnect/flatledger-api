using System.Text.Json.Serialization;

namespace SocietyLedger.Application.DTOs.Dashboard
{
    /// <summary>
    /// Top-level response from public.get_dashboard_data()
    /// </summary>
    public class DashboardResponseDto
    {
        [JsonPropertyName("period")]
        public PeriodInfo Period { get; set; } = new();

        [JsonPropertyName("snapshot")]
        public SnapshotInfo Snapshot { get; set; } = new();

        [JsonPropertyName("trend_meta")]
        public TrendMetaInfo? TrendMeta { get; set; }

        [JsonPropertyName("trends")]
        public List<MonthlyTrend> Trends { get; set; } = new();

        [JsonPropertyName("top_defaulters")]
        public List<TopDefaulter> TopDefaulters { get; set; } = new();

        [JsonPropertyName("recent_activity")]
        public List<RecentActivityItem> RecentActivity { get; set; } = new();

        [JsonPropertyName("expense_breakdown")]
        public List<ExpenseBreakdownItem> ExpenseBreakdown { get; set; } = new();

        [JsonPropertyName("insights")]
        public List<string> Insights { get; set; } = new();

        [JsonPropertyName("flat_summary")]
        public FlatSummaryDto FlatSummary { get; set; } = new();
    }

    public class TrendMetaInfo
    {
        [JsonPropertyName("window_months")]
        public int WindowMonths { get; set; }

        [JsonPropertyName("end_month")]
        public string EndMonth { get; set; } = string.Empty;
    }
}
