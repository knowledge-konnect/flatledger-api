namespace SocietyLedger.Application.DTOs.Admin
{
    public class AdminUserDto
    {
        public long Id { get; set; }
        public Guid PublicId { get; set; }
        public long SocietyId { get; set; }
        public string? SocietyName { get; set; }
        public string Name { get; set; } = null!;
        public string? Email { get; set; }
        public string? Mobile { get; set; }
        public string? Username { get; set; }
        public short RoleId { get; set; }
        public bool IsActive { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? LastLogin { get; set; }
        public DateTime CreatedAt { get; set; }
        // Denormalized subscription fields stored on the user row
        public string? SubscriptionStatus { get; set; }
        public string? SubscriptionPlan { get; set; }
        public DateTime? TrialEndsDate { get; set; }
        public DateTime? NextBillingDate { get; set; }
    }
}
