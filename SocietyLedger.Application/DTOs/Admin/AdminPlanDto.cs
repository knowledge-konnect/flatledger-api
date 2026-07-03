namespace SocietyLedger.Application.DTOs.Admin
{
    public class AdminPlanDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        /// <summary>Total price charged per billing cycle (e.g. ₹2999/year).</summary>
        public decimal Price { get; set; }
        /// <summary>Per-month display amount shown on pricing cards (e.g. ₹249/month).</summary>
        public decimal MonthlyAmount { get; set; }
        public decimal? DiscountPercentage { get; set; }
        public string Currency { get; set; } = null!;
        public bool? IsActive { get; set; }
        public bool IsPopular { get; set; }
        public string? PlanGroup { get; set; }
        public int DisplayOrder { get; set; }
        public int? MaxFlats { get; set; }
        public int DurationMonths { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
