namespace SocietyLedger.Application.DTOs.Admin
{
    public class AdminSubscriptionDto
    {
        public Guid Id { get; set; }
        public long UserId { get; set; }
        public string UserName { get; set; } = null!;
        public string? UserEmail { get; set; }
        public long SocietyId { get; set; }
        public string? SocietyName { get; set; }
        public Guid PlanId { get; set; }
        public string PlanName { get; set; } = null!;
        public string Status { get; set; } = null!;
        public decimal SubscribedAmount { get; set; }
        public string? Currency { get; set; }
        public DateTime? CurrentPeriodStart { get; set; }
        public DateTime? CurrentPeriodEnd { get; set; }
        public DateTime? TrialStart { get; set; }
        public DateTime? TrialEnd { get; set; }
        public DateTime? CancelledAt { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
