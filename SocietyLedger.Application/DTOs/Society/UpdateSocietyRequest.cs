namespace SocietyLedger.Application.DTOs.Society
{
    /// <summary>
    /// Request DTO for updating a society's profile.
    /// PUT /societies/{publicId}
    /// Only the society admin of that society may call this endpoint.
    /// </summary>
    public class UpdateSocietyRequest
    {
        public string Name { get; set; } = null!;
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Country { get; set; }
        public string? Pincode { get; set; }
    }
}
