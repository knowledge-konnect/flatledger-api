namespace SocietyLedger.Application.DTOs.Auth
{
    public class VerifyEmailResponse
    {
        public bool Allowed { get; set; }
        /// <summary>Masked email address (e.g., "j***@example.com") for display purposes.</summary>
        public string? MaskedEmail { get; set; }
        /// <summary>Optional message to display to user.</summary>
        public string? Message { get; set; }
    }
}
