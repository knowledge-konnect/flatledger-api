namespace SocietyLedger.Application.DTOs.Admin
{
    public class AdminSocietyDto
    {
        public long Id { get; set; }
        public Guid PublicId { get; set; }
        public string Name { get; set; } = null!;
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Pincode { get; set; }
        public string Currency { get; set; } = null!;
        public string DefaultMaintenanceCycle { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public DateOnly OnboardingDate { get; set; }
        // Aggregate counts — populated by detail endpoint
        public int FlatCount { get; set; }
        public int ActiveFlatCount { get; set; }
        public int UserCount { get; set; }
        public int ActiveUserCount { get; set; }
        public AdminSocietySubscriptionSummary? ActiveSubscription { get; set; }
    }

    public class AdminSocietySubscriptionSummary
    {
        public Guid Id { get; set; }
        public string PlanName { get; set; } = null!;
        public string Status { get; set; } = null!;
        public decimal SubscribedAmount { get; set; }
        public string? Currency { get; set; }
        public DateTime? CurrentPeriodEnd { get; set; }
        public DateTime? TrialEnd { get; set; }
    }
}