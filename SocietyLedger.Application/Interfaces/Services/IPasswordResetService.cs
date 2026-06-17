using SocietyLedger.Application.DTOs.Auth;

namespace SocietyLedger.Application.Interfaces.Services
{
    public interface IPasswordResetService
    {
        Task<bool> InitiatePasswordResetAsync(ForgotPasswordRequest request, string ipAddress);
        Task<bool> ResetPasswordAsync(ResetPasswordRequest request, string ipAddress);
    }
}