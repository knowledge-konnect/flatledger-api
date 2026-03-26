namespace SocietyLedger.Application.DTOs.Admin
{
    public class AdminPlanCreateRequest
    {
        public string Name { get; set; } = null!;
        public decimal MonthlyAmount { get; set; }
        public string Currency { get; set; } = null!;
        public int DurationMonths { get; set; }
    }
}