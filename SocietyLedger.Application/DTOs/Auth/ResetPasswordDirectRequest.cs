namespace SocietyLedger.Application.DTOs.Auth
{
    public class ResetPasswordDirectRequest
    {
        public string Email { get; set; } = null!;
        public string NewPassword { get; set; } = null!;
        public string ConfirmPassword { get; set; } = null!;
    }
}
