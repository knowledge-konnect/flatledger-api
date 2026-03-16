namespace SocietyLedger.Application.DTOs.Admin
{
    public class AdminSubscriptionUpdateRequest
    {
        public Guid PlanId { get; set; }
        public string Status { get; set; } = null!;
        public DateTime? CurrentPeriodStart { get; set; }
        public DateTime? CurrentPeriodEnd { get; set; }
    }
}
