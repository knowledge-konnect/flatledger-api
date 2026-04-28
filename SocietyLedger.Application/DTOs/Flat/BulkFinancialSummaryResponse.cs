namespace SocietyLedger.Application.DTOs.Flat
{
    public class BulkFinancialSummaryResponse
    {
        public Dictionary<string, FlatFinancialSummaryResponse> Summaries { get; set; } = new();
    }
}
