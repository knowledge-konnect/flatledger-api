namespace SocietyLedger.Application.DTOs.Admin
{
    public class AdminPlanUpdateRequest
    {
        public string Name { get; set; } = null!;
        public decimal Price { get; set; }
        public string Currency { get; set; } = null!;
        public bool? IsActive { get; set; }
        public int DurationMonths { get; set; }
        public int MaxFlats { get; set; }
        public string PlanGroup { get; set; } = null!;
        public bool IsPopular { get; set; }
        public string? Description { get; set; }
        public int? DiscountPercentage { get; set; }
        public int DisplayOrder { get; set; }
    }
}