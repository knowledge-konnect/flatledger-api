namespace SocietyLedger.Application.DTOs.Flat
{
    public class BulkFinancialSummaryRequest
    {
        public List<Guid> FlatPublicIds { get; set; } = new();
    }
}
