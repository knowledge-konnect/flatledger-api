using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SocietyLedger.Application.DTOs;
using SocietyLedger.Application.DTOs.Auth;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Shared;

namespace SocietyLedger.Infrastructure.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUserRepository _userRepo;
        private readonly IRoleRepository _roleRepo;
        private readonly ISocietyRepository _societyRepo;
        private readonly ITokenService _tokenService;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IEmailService _emailService;
        private readonly PasswordHasher _hasher;
        private readonly ILogger<AuthService> _logger;
        private readonly AppDbContext _db;
        private readonly IRefreshTokenRepository _refreshTokenRepo;

        public AuthService(
            IUserRepository userRepo,
            IRoleRepository roleRepo,
            ISocietyRepository societyRepo,
            ITokenService tokenService,
            ISubscriptionService subscriptionService,
            IEmailService emailService,
            PasswordHasher hasher,
            ILogger<AuthService> logger,
            AppDbContext db,
            IRefreshTokenRepository refreshTokenRepo)
        {
            _userRepo = userRepo;
            _roleRepo = roleRepo;
            _societyRepo = societyRepo;
            _tokenService = tokenService;
            _subscriptionService = subscriptionService;
            _emailService = emailService;
            _hasher = hasher;
            _logger = logger;
            _db = db;
            _refreshTokenRepo = refreshTokenRepo;
        }

        /// <summary>
        /// Validates credentials, rotates tokens, and returns a fully populated <see cref="LoginResponse"/>.
        /// Uses <c>ExecuteUpdateAsync</c> to update <c>last_login</c> without a tracked SELECT, which
        /// avoids a round-trip inside a transaction and prevents pgBouncer connection timeouts.
        /// </summary>
        public async Task<LoginResponse> LoginAsync(LoginRequest request, string ipAddress)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var user = await _userRepo.GetByUsernameOrEmailAsync(request.UsernameOrEmail);
            if (user == null)
                throw new AuthenticationException("Invalid credentials.");

            if (!_hasher.Verify(user.PasswordHash, request.Password))
                throw new AuthenticationException("Invalid credentials.");

            if (!user.IsActive)
                throw new AuthenticationException("User account is inactive.");

            var userRole = user.Role ?? throw new AuthenticationException("User role not configured.");
            var tokenClaims = new TokenClaims(
                UserId: user.Id,
                UserPublicId: user.PublicId,
                Email: user.Email ?? string.Empty,
                Name: user.Name,
                SocietyPublicId: user.SocietyPublicId,
                RoleId: userRole.Id,
                RoleCode: userRole.Code,
                RoleDisplayName: userRole.DisplayName);
            var accessToken = _tokenService.GenerateAccessToken(tokenClaims, out var accessExpires);
            var refreshPair = _tokenService.GenerateRefreshToken();

            var refreshEntity = new RefreshTokenEntity
            {
                UserId        = user.Id,
                TokenHash     = _tokenService.HashToken(refreshPair.Token),
                JwtId         = Guid.NewGuid().ToString(),
                ExpiresAt     = refreshPair.ExpiresAt,
                CreatedAt     = DateTime.UtcNow,
                CreatedByIp   = ipAddress,
                IsRevoked     = false
            };

            var now = DateTime.UtcNow;

            // Direct UPDATE avoids a SELECT inside a transaction that can time out on pgBouncer.
            await _userRepo.UpdateLastLoginAsync(user.Id, now);

            await _refreshTokenRepo.AddAsync(refreshEntity);
            await _refreshTokenRepo.SaveChangesAsync();

            _logger.LogInformation("User {UserPublicId} logged in from {IP}", user.PublicId, ipAddress);

            return new LoginResponse
            {
                AccessToken = accessToken,
                AccessTokenExpiresAt = accessExpires,
                RefreshToken = refreshPair.Token,
                RefreshTokenExpiresAt = refreshPair.ExpiresAt,
                Roles = new[] { new RoleDto { Id = userRole.Id, Code = userRole.Code, DisplayName = userRole.DisplayName } },
                UserPublicId = user.PublicId,
                UserName = user.Name,
                Role = userRole.Code,
                RoleDisplayName = userRole.DisplayName,
                SocietyPublicId = user.SocietyPublicId,
                SocietyName = user.SocietyName,
                ForcePasswordChange = user.ForcePasswordChange
            };
        }


        /// <summary>
        /// Creates a new Society + SocietyAdmin user inside a single transaction, then issues tokens.
        /// Trial subscription creation is included in the transaction — if it fails the entire
        /// registration is rolled back so users are never left in a state where they can log in
        /// but have no subscription.
        /// </summary>
        public async Task<RegisterResponse> RegisterAsync(RegisterRequest request, string ipAddress)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            await using var tx = await _db.Database.BeginTransactionAsync();

            var existingUser = await _userRepo.GetByUsernameOrEmailAsync(request.Email);
            if (existingUser != null)
                throw new DuplicateException("User", "email");

            var society = Society.Create(
                request.SocietyName ?? "Default Society",
                request.SocietyAddress
            );

            await _societyRepo.AddAsync(society);

            var role = await _roleRepo.GetByCodeAsync(RoleCodes.SocietyAdmin)
                ?? throw new NotFoundException("Role", RoleCodes.SocietyAdmin);

            // PublicId, CreatedAt, UpdatedAt are set by PostgreSQL database defaults.
            var user = new User
            {
                SocietyId = society.Id,
                Name = request.Name,
                Email = request.Email,
                PasswordHash = _hasher.Hash(request.Password),
                RoleId = role.Id,
                IsActive = true
            };

            await _userRepo.AddAsync(user);

            var tokenClaimsReg = new TokenClaims(
                UserId: user.Id,
                UserPublicId: user.PublicId,
                Email: user.Email ?? string.Empty,
                Name: user.Name,
                SocietyPublicId: society.PublicId,
                RoleId: role.Id,
                RoleCode: role.Code,
                RoleDisplayName: role.DisplayName);
            var accessToken = _tokenService.GenerateAccessToken(tokenClaimsReg, out var accessExpires);
            var refreshPair = _tokenService.GenerateRefreshToken();

            var refreshEntity = new RefreshTokenEntity
            {
                UserId        = user.Id,
                TokenHash     = _tokenService.HashToken(refreshPair.Token),
                JwtId         = Guid.NewGuid().ToString(),
                ExpiresAt     = refreshPair.ExpiresAt,
                CreatedAt     = DateTime.UtcNow,
                CreatedByIp   = ipAddress,
                IsRevoked     = false
            };

            await _refreshTokenRepo.AddAsync(refreshEntity);
            await _db.SaveChangesAsync();

            // Trial creation is inside the transaction — if it fails the whole registration
            // rolls back, preventing orphaned users with no subscription.
            try
            {
                await _subscriptionService.CreateTrialSubscriptionAsync(user.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Trial subscription creation failed for user {UserId} — rolling back registration", user.Id);
                await tx.RollbackAsync();
                throw new AppException("Registration failed: could not create trial subscription. Please try again.");
            }

            await tx.CommitAsync();

            _logger.LogInformation(
                "New user {UserPublicId} registered new society {SocietyId} from {IP}",
                user.PublicId, society.Id, ipAddress);

            return new RegisterResponse
            {
                AccessToken = accessToken,
                AccessTokenExpiresAt = accessExpires,
                RefreshToken = refreshPair.Token,
                RefreshTokenExpiresAt = refreshPair.ExpiresAt,
                Roles = new[] { new RoleDto { Id = role.Id, Code = role.Code, DisplayName = role.DisplayName } },
                UserPublicId = user.PublicId,
                UserName = user.Name,
                Role = role.Code,
                RoleDisplayName = role.DisplayName,
                SocietyPublicId = society.PublicId,
                SocietyName = society.Name,
                ForcePasswordChange = false,
                SocietyId = society.Id,
                UserId = user.Id
            };
        }


        /// <summary>
        /// Rotates the refresh token: revokes the old one and issues a new pair inside a transaction.
        /// </summary>
        public async Task<LoginResponse> RefreshTokenAsync(string token, string ipAddress)
        {
            var hashed = _tokenService.HashToken(token);

            var rt = await _refreshTokenRepo.GetByHashAsync(hashed);

            if (rt == null || rt.IsRevoked || rt.ExpiresAt <= DateTime.UtcNow)
                throw new AuthenticationException("Invalid or expired refresh token");

            var newPair = _tokenService.GenerateRefreshToken();

            var newRt = new RefreshTokenEntity
            {
                UserId              = rt.UserId,
                TokenHash           = _tokenService.HashToken(newPair.Token),
                JwtId               = Guid.NewGuid().ToString(),
                ExpiresAt           = newPair.ExpiresAt,
                CreatedAt           = DateTime.UtcNow,
                CreatedByIp         = ipAddress,
                IsRevoked           = false,
                ReplacedByTokenHash = rt.TokenHash
            };

            // Atomically revoke the old token and persist the new one.
            await using var transaction = await _db.Database.BeginTransactionAsync();

            try
            {
                await _refreshTokenRepo.RevokeAsync(hashed, DateTime.UtcNow);
                await _refreshTokenRepo.AddAsync(newRt);
                await _refreshTokenRepo.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            var refreshRole = rt.User;
            var tokenClaimsRefresh = new TokenClaims(
                UserId: rt.UserId,
                UserPublicId: rt.User?.PublicId ?? Guid.Empty,
                Email: rt.User?.Email ?? string.Empty,
                Name: rt.User?.Name ?? string.Empty,
                SocietyPublicId: rt.User?.SocietyPublicId ?? Guid.Empty,
                RoleId: rt.User?.RoleId ?? 0,
                RoleCode: rt.User?.RoleCode ?? string.Empty,
                RoleDisplayName: rt.User?.RoleDisplayName ?? string.Empty);
            var accessToken = _tokenService.GenerateAccessToken(tokenClaimsRefresh, out var accessExpires);

            _logger.LogInformation("Refresh token rotated for user {UserId} from {Ip}", rt.UserId, ipAddress);

            return new LoginResponse
            {
                AccessToken = accessToken,
                AccessTokenExpiresAt = accessExpires,
                RefreshToken = newPair.Token,
                RefreshTokenExpiresAt = newPair.ExpiresAt,
                Roles = rt.User != null
                    ? new[] { new RoleDto { Id = rt.User.RoleId, Code = rt.User.RoleCode ?? string.Empty, DisplayName = rt.User.RoleDisplayName ?? string.Empty } }
                    : Enumerable.Empty<RoleDto>(),
                UserPublicId = rt.User?.PublicId ?? Guid.Empty,
                UserName = rt.User?.Name ?? string.Empty,
                Role = rt.User?.RoleCode,
                RoleDisplayName = rt.User?.RoleDisplayName,
                SocietyPublicId = rt.User?.SocietyPublicId ?? Guid.Empty,
                SocietyName = rt.User?.SocietyName ?? string.Empty,
                ForcePasswordChange = rt.User?.ForcePasswordChange ?? false
            };
        }

        /// <summary>
        /// Revokes a refresh token by its hashed value. Silently no-ops if the token is not found.
        /// </summary>
        public async Task RevokeRefreshTokenAsync(string token, string ipAddress)
        {
            var hashed = _tokenService.HashToken(token);
            await _refreshTokenRepo.RevokeAsync(hashed, DateTime.UtcNow);
            _logger.LogInformation("Refresh token revoked from {Ip}", ipAddress);
        }

        /// <summary>
        /// Verifies the current password, hashes the new one, and clears the force-password-change flag.
        /// </summary>
        public async Task<ChangePasswordResponse> ChangePasswordAsync(long userId, ChangePasswordRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null)
                throw new NotFoundException("User", userId.ToString());

            if (!user.IsActive)
                throw new ConflictException("User account is inactive.");

            if (!_hasher.Verify(user.PasswordHash, request.CurrentPassword))
                throw new ValidationException(
                    ErrorMessages.VALIDATION_FAILED,
                    new Dictionary<string, string[]>
                    {
                        ["currentPassword"] = ["Current password is incorrect."]
                    });

            var newPasswordHash = _hasher.Hash(request.NewPassword);
            user.PasswordHash = newPasswordHash;
            user.ForcePasswordChange = false;
            user.UpdatedAt = DateTime.UtcNow;

            await _userRepo.UpdateAsync(user);
            await _userRepo.SaveChangesAsync();

            _logger.LogInformation("Password changed successfully for user {UserId}", userId);

            return new ChangePasswordResponse
            {
                Message = "Password changed successfully.",
                ForcePasswordChange = false
            };
        }

        /// <summary>
        /// Initiates forgot password flow: generates a cryptographically-secure token, hashes it, stores on user,
        /// and sends reset email. Always succeeds (returns 200) to avoid account enumeration.
        /// </summary>
        public async Task ForgotPasswordAsync(string email, string resetLinkTemplate, string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentNullException(nameof(email));

            var user = await _userRepo.GetByEmailAsync(email);
            if (user == null)
            {
                // Security: do not reveal if user exists. Just return success.
                _logger.LogInformation("Forgot password request for non-existent user {Email} from {IP}", email, ipAddress);
                return;
            }

            if (!user.IsActive)
            {
                _logger.LogInformation("Forgot password request for inactive user {UserId} from {IP}", user.Id, ipAddress);
                return;
            }

            // Generate a secure random token (same method as refresh tokens).
            var tokenPair = _tokenService.GenerateRefreshToken();
            var token = tokenPair.Token;

            // Hash the token (only store hash in DB).
            var tokenHash = _tokenService.HashToken(token);

            // Store hash and expiry on user entity. Token expires in 15 minutes.
            user.PasswordResetTokenHash = tokenHash;
            user.PasswordResetExpiresAt = DateTime.UtcNow.AddMinutes(15);
            user.UpdatedAt = DateTime.UtcNow;

            await _userRepo.UpdateAsync(user);
            await _userRepo.SaveChangesAsync();

            // Send email with the unhashed token (the user needs to use this token).
            var resetLink = string.Format(resetLinkTemplate, Uri.EscapeDataString(token));
            try
            {
                await _emailService.SendPasswordResetEmailAsync(user.Email ?? "", user.Name, resetLink);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password reset email to {Email}", email);
                // Don't throw; ForgotPassword always returns 200 success for security.
            }

            _logger.LogInformation("Password reset token generated for user {UserId} (email: {Email}) from {IP}", user.Id, email, ipAddress);
        }

        /// <summary>
        /// Validates a password reset token: checks hash match, expiry, and existence.
        /// Throws if invalid or expired.
        /// </summary>
        public async Task ValidatePasswordResetTokenAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ValidationException("Invalid or expired token.");

            var tokenHash = _tokenService.HashToken(token);

            var user = await _userRepo.GetByPasswordResetTokenHashAsync(tokenHash);

            if (user == null)
                throw new ValidationException("Invalid or expired token.");

            if (user.PasswordResetExpiresAt == null || user.PasswordResetExpiresAt < DateTime.UtcNow)
                throw new ValidationException("Invalid or expired token.");

            _logger.LogInformation("Password reset token validated for user {UserId}", user.Id);
        }

        /// <summary>
        /// Resets password using token: validates token, hashes new password, clears the token (single-use),
        /// and optionally returns JWT for auto-login.
        /// </summary>
        public async Task<PasswordResetResponse> ResetPasswordAsync(string token, string newPassword, string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ValidationException("Token is required.");

            var tokenHash = _tokenService.HashToken(token);

            var user = await _userRepo.GetByPasswordResetTokenHashAsync(tokenHash);

            if (user == null || user.PasswordResetExpiresAt == null || user.PasswordResetExpiresAt < DateTime.UtcNow)
                throw new ValidationException("Invalid or expired token.");

            var newPasswordHash = _hasher.Hash(newPassword);
            await _userRepo.SetPasswordAndClearResetTokenAsync(user.Id, newPasswordHash);

            _logger.LogInformation("Password reset successfully for user {UserId} from {IP}", user.Id, ipAddress);

            var role = user.Role;

            if (role != null)
            {
                var tokenClaims = new TokenClaims(
                    UserId: user.Id,
                    UserPublicId: user.PublicId,
                    Email: user.Email ?? string.Empty,
                    Name: user.Name,
                    SocietyPublicId: user.SocietyPublicId,
                    RoleId: role.Id,
                    RoleCode: role.Code,
                    RoleDisplayName: role.DisplayName);

                var accessToken = _tokenService.GenerateAccessToken(tokenClaims, out var accessExpires);

                return new PasswordResetResponse
                {
                    Ok = true,
                    Message = "Password reset successfully.",
                    AccessToken = accessToken,
                    AccessTokenExpiresAt = accessExpires
                };
            }

            return new PasswordResetResponse
            {
                Ok = true,
                Message = "Password reset successfully."
            };
        }
    }
}
