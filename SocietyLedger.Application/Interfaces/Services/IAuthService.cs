using SocietyLedger.Application.DTOs.Auth;

namespace SocietyLedger.Application.Interfaces.Services
{
    public interface IAuthService
    {
        Task<LoginResponse> LoginAsync(LoginRequest request, string ipAddress);
        Task<RegisterResponse> RegisterAsync(RegisterRequest request, string ipAddress);
        Task<LoginResponse> RefreshTokenAsync(string token, string ipAddress);
        Task RevokeRefreshTokenAsync(string token, string ipAddress);
        Task<ChangePasswordResponse> ChangePasswordAsync(long userId, ChangePasswordRequest request);

        /// <summary>
        /// Checks if an email exists in the system. Returns true if found and active, false otherwise.
        /// Used by the direct password reset flow (no email/token required).
        /// </summary>
        Task<bool> CheckEmailExistsAsync(string email);

        /// <summary>
        /// Resets password directly: verifies email exists, sets new password, returns access token for auto-login.
        /// No token or email required — caller must have already verified the email via CheckEmailExistsAsync.
        /// </summary>
        Task<PasswordResetResponse> ResetPasswordDirectAsync(ResetPasswordDirectRequest request, string ipAddress);
    }
}
