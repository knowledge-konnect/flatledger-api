namespace SocietyLedger.Application.DTOs.Admin
{
    public class AdminSocietyUpdateRequest
    {
        public string Name { get; set; } = null!;
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Pincode { get; set; }
        public string Currency { get; set; } = null!;
        public string DefaultMaintenanceCycle { get; set; } = null!;
        public bool? IsDeleted { get; set; }
    }
}