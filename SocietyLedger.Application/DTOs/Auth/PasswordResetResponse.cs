namespace SocietyLedger.Application.DTOs.Auth
{
    public class PasswordResetResponse
    {
        public bool Ok { get; set; } = true;
        public string? Message { get; set; }
        /// <summary>Optional: auto-login with JWT access token after successful password reset.</summary>
        public string? AccessToken { get; set; }
        public DateTime? AccessTokenExpiresAt { get; set; }
    }
}
