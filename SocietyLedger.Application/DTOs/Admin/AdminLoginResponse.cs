namespace SocietyLedger.Application.DTOs.Admin
{
    /// <summary>
    /// Returned on successful admin login. Contains only what the
    /// admin UI needs — no society fields, no role arrays.
    /// </summary>
    public class AdminLoginResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public DateTime AccessTokenExpiresAt { get; set; }
        public Guid AdminPublicId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }
}
