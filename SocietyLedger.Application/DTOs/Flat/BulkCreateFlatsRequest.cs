namespace SocietyLedger.Application.DTOs.Flat
{
    public record BulkCreateFlatsRequest(
        List<BulkCreateFlatItemDto> Flats,
        bool SkipBilling = false);
}
