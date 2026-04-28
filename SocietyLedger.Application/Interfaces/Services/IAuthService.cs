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
        /// <summary>Initiates forgot password flow: generates token, hashes it, and sends reset email. Always returns success for security.</summary>
        Task ForgotPasswordAsync(string email, string resetLinkTemplate, string ipAddress);
        /// <summary>Validates a password reset token (check hash, expiry, exists).</summary>
        Task ValidatePasswordResetTokenAsync(string token);
        /// <summary>Resets password using token. Token is cleared after successful reset (single-use).</summary>
        Task<PasswordResetResponse> ResetPasswordAsync(string token, string newPassword, string ipAddress);
    }
}
