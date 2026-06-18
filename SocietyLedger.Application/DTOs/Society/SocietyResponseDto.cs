using System;

namespace SocietyLedger.Application.DTOs.Society
{
    /// <summary>
    /// Response DTO for a society. Returned by GET /societies and GET /societies/{publicId}.
    /// </summary>
    public class SocietyResponseDto
    {
        public Guid PublicId { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Country { get; set; }
        public string? Pincode { get; set; }
        public DateOnly OnboardingDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
