namespace SocietyLedger.Application.DTOs.Auth
{
    public class ForgotPasswordResponse
    {
        public string Message { get; set; } = null!;
        /// <summary>
        /// The password reset token. Present only when the request is valid.
        /// The frontend uses this to redirect the user to the reset-password page.
        /// </summary>
        public string? ResetToken { get; set; }
    }
}
