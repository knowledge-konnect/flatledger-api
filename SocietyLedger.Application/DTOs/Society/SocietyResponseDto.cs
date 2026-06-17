using System;

namespace SocietyLedger.Application.DTOs.Society
{
    public class SocietyResponseDto
    {
        public Guid PublicId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Country { get; set; }
        public string? Pincode { get; set; }
        public DateOnly OnboardingDate { get; set; }
    }
}