namespace SocietyLedger.Application.DTOs.Admin
{
    public class AdminPlanCreateRequest
    {
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        /// <summary>Total price charged per billing cycle.</summary>
        public decimal Price { get; set; }
        /// <summary>Per-month display amount for pricing cards.</summary>
        public decimal MonthlyAmount { get; set; }
        public decimal? DiscountPercentage { get; set; }
        public string Currency { get; set; } = "INR";
        public bool IsActive { get; set; } = true;
        public bool IsPopular { get; set; }
        public string? PlanGroup { get; set; }
        public int DisplayOrder { get; set; }
        public int? MaxFlats { get; set; }
        public int DurationMonths { get; set; } = 1;
    }
}
