using System.Text.Json.Serialization;

namespace SocietyLedger.Application.DTOs.Admin
{
    public class AdminPlanCreateRequest
    {
        public string Name { get; set; } = null!;
        public decimal MonthlyAmount { get; set; }
        public string Currency { get; set; } = null!;
        public int DurationMonths { get; set; } = 1;
        public int MaxFlats { get; set; }
        public string? PlanGroup { get; set; }
        public int? DiscountPercentage { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsPopular { get; set; }
        public string? Description { get; set; }

        [JsonPropertyName("price")]
        public decimal Price
        {
            get => MonthlyAmount;
            set => MonthlyAmount = value;
        }
    }
}
