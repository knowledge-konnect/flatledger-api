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
    }
}