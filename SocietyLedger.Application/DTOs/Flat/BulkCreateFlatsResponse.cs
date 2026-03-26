namespace SocietyLedger.Application.DTOs.Flat
{
    public record BulkFlatFailure(int Index, string FlatNo, string Error);

    public record BulkCreateFlatsResponse(
        List<FlatResponseDto> Succeeded,
        List<BulkFlatFailure> Failed
    );
}
