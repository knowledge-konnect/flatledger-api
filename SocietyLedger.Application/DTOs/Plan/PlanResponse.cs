namespace SocietyLedger.Application.DTOs.Plan
{
    public class PlanResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        /// <summary>Total price charged per billing cycle. Used for billing and payment.</summary>
        public decimal Price { get; set; }
        /// <summary>Per-month display amount shown on pricing cards.</summary>
        public decimal MonthlyAmount { get; set; }
        public string Currency { get; set; } = null!;
        public bool? IsActive { get; set; }
        public int? MaxFlats { get; set; }
        public int DurationMonths { get; set; }
        public string? PlanGroup { get; set; }
        public bool IsPopular { get; set; }
        public int DisplayOrder { get; set; }
        public string? Description { get; set; }
        public int? DiscountPercentage { get; set; }
        public DateTime? CreatedAt { get; set; }
    }
}
