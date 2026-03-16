namespace SocietyLedger.Application.DTOs.Admin
{
    public class AdminLoginRequest
    {
        public string Email { get; set; } = null!;
        public string Password { get; set; } = null!;
    }
}
