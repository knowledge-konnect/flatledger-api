namespace SocietyLedger.Application.DTOs.Admin
{
    public class AdminPlanDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public decimal Price { get; set; }
        public decimal MonthlyAmount { get; set; }
        public string Currency { get; set; } = null!;
        public bool? IsActive { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int DurationMonths { get; set; }
        public int MaxFlats { get; set; }
        public string? PlanGroup { get; set; }
        public int? DiscountPercentage { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsPopular { get; set; }
        public string? Description { get; set; }
    }
}
