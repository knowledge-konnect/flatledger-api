using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Application.Interfaces.Services.Admin;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Persistence.Contexts;

namespace SocietyLedger.Infrastructure.Services.Admin
{
    public class AdminAuthService : IAdminAuthService
    {
        private readonly AppDbContext _db;
        private readonly PasswordHasher _hasher;
        private readonly ITokenService _tokenService;
        private readonly ILogger<AdminAuthService> _logger;

        public AdminAuthService(
            AppDbContext db,
            PasswordHasher hasher,
            ITokenService tokenService,
            ILogger<AdminAuthService> logger)
        {
            _db           = db;
            _hasher       = hasher;
            _tokenService = tokenService;
            _logger       = logger;
        }

        /// <inheritdoc/>
        public async Task<AdminLoginResponse> LoginAsync(AdminLoginRequest request, string ipAddress)
        {
            // Single indexed query — no tracking needed for auth check
            var admin = await _db.admin_users
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.email == request.Email.ToLowerInvariant());

            // Constant-time rejection: verify even on null to prevent user enumeration
            var dummyHash = "$2b$12$invalidhashinvalidhashinvalidhashinvalidhashinvalidhash";
            var hashToVerify = admin?.password_hash ?? dummyHash;
            var passwordValid = _hasher.Verify(hashToVerify, request.Password);

            if (admin == null || !passwordValid)
                throw new AuthenticationException("Invalid credentials.");

            if (!admin.is_active)
                throw new AuthenticationException("Admin account is inactive.");

            var tokenClaims = new AdminTokenClaims(
                AdminId:       admin.id,
                AdminPublicId: admin.public_id,
                Email:         admin.email,
                Name:          admin.name);

            var accessToken = _tokenService.GenerateAdminAccessToken(tokenClaims, out var expiresAt);

            // Update last_login without blocking the login response.
            // Swallow exceptions — a failed timestamp update must never fail a successful login.
            _ = Task.Run(async () =>
            {
                try
                {
                    await _db.admin_users
                        .Where(a => a.id == admin.id)
                        .ExecuteUpdateAsync(s => s.SetProperty(a => a.last_login, DateTime.UtcNow));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update last_login for admin {AdminId}", admin.id);
                }
            });

            _logger.LogInformation("Admin {Email} logged in from {IP}", admin.email, ipAddress);

            return new AdminLoginResponse
            {
                AccessToken         = accessToken,
                AccessTokenExpiresAt = expiresAt,
                AdminPublicId       = admin.public_id,
                Name                = admin.name,
                Email               = admin.email,
            };
        }

        /// <inheritdoc/>
        public async Task<AdminProfileDto> GetProfileAsync(long adminId)
        {
            var admin = await _db.admin_users
                .AsNoTracking()
                .Where(a => a.id == adminId)
                .Select(a => new AdminProfileDto
                {
                    PublicId  = a.public_id,
                    Name      = a.name,
                    Email     = a.email,
                    IsActive  = a.is_active,
                    LastLogin = a.last_login,
                    CreatedAt = a.created_at,
                })
                .FirstOrDefaultAsync();

            if (admin == null)
                throw new NotFoundException("Admin", adminId.ToString());

            return admin;
        }
    }
}
