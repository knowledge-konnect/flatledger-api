namespace SocietyLedger.Application.DTOs.Admin
{
    public class AdminPaymentDto
    {
        public long Id { get; set; }
        public Guid PublicId { get; set; }
        public long SocietyId { get; set; }
        public long? BillId { get; set; }
        public long? FlatId { get; set; }
        public decimal Amount { get; set; }
        public DateTime? DatePaid { get; set; }
        public string? ModeCode { get; set; }
        public string? Reference { get; set; }
        public string? PaymentType { get; set; }
        public string? RazorpayPaymentId { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
