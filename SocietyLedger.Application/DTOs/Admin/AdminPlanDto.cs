namespace SocietyLedger.Application.DTOs.Admin
{
    public class AdminPlanDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public decimal MonthlyAmount { get; set; }
        public string Currency { get; set; } = null!;
        public bool? IsActive { get; set; }
        public DateTime? CreatedAt { get; set; }
        public int DurationMonths { get; set; }
    }
}