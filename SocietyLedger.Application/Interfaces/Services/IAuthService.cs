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
        /// Initiates password reset: always returns the same message (no email enumeration).
        /// Sends a reset link when an active account exists for the email.
        /// </summary>
        Task<ForgotPasswordResponse> RequestPasswordResetAsync(ForgotPasswordRequest request);

        /// <summary>
        /// Completes password reset using the single-use token from the email link.
        /// </summary>
        Task<PasswordResetResponse> ResetPasswordWithTokenAsync(ResetPasswordRequest request, string ipAddress);
    }
}
