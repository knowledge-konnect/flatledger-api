namespace SocietyLedger.Application.DTOs.Admin
{
    public class AdminBillDto
    {
        public long Id { get; set; }
        public Guid PublicId { get; set; }
        public long SocietyId { get; set; }
        public string? SocietyName { get; set; }
        public long FlatId { get; set; }
        public string? FlatNo { get; set; }
        public string Period { get; set; } = null!;
        public decimal Amount { get; set; }
        public DateOnly? DueDate { get; set; }
        public string StatusCode { get; set; } = null!;
        public decimal? PaidAmount { get; set; }
        public decimal? BalanceAmount { get; set; }
        public DateTime GeneratedAt { get; set; }
        public bool IsDeleted { get; set; }
    }
}
