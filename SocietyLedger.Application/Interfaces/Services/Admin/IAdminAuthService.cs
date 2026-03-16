using SocietyLedger.Application.DTOs.Admin;

namespace SocietyLedger.Application.Interfaces.Services.Admin
{
    public interface IAdminAuthService
    {
        /// <summary>
        /// Validates admin credentials and returns a JWT access token.
        /// No refresh token — admin sessions are short-lived by design.
        /// </summary>
        Task<AdminLoginResponse> LoginAsync(AdminLoginRequest request, string ipAddress);

        /// <summary>
        /// Returns the profile of the authenticated admin by their internal id.
        /// </summary>
        Task<AdminProfileDto> GetProfileAsync(long adminId);
    }
}
