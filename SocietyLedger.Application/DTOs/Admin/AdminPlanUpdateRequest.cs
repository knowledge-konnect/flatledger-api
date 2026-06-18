using System.Text.Json.Serialization;

namespace SocietyLedger.Application.DTOs.Admin
{
    public class AdminPlanUpdateRequest
    {
        public string Name { get; set; } = null!;
        public decimal Price { get; set; }
        public string Currency { get; set; } = null!;
        public bool? IsActive { get; set; }
        public int DurationMonths { get; set; }
    }
}
