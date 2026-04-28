namespace SocietyLedger.Application.DTOs.Flat
{
    public class PagedFlatsResponse
    {
        public List<FlatResponseDto> Content { get; set; } = new();
        public long TotalElements { get; set; }
        public int TotalPages { get; set; }
        public int Page { get; set; }
        public int Size { get; set; }
    }
}
