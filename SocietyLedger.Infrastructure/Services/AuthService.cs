using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SocietyLedger.Application.DTOs;
using SocietyLedger.Application.DTOs.Auth;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Security;
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
        private readonly PasswordHasher _hasher;
        private readonly ILogger<AuthService> _logger;
        private readonly AppDbContext _db;
        private readonly IRefreshTokenRepository _refreshTokenRepo;
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;
        private readonly IEmailService _emailService;

        private const string PasswordResetGenericMessage =
            "If an account exists for this email, password reset instructions have been sent.";

        public AuthService(
            IUserRepository userRepo,
            IRoleRepository roleRepo,
            ISocietyRepository societyRepo,
            ITokenService tokenService,
            ISubscriptionService subscriptionService,
            PasswordHasher hasher,
            ILogger<AuthService> logger,
            AppDbContext db,
            IRefreshTokenRepository refreshTokenRepo,
            IConfiguration configuration,
            IHostEnvironment environment,
            IEmailService emailService)
        {
            _userRepo = userRepo;
            _roleRepo = roleRepo;
            _societyRepo = societyRepo;
            _tokenService = tokenService;
            _subscriptionService = subscriptionService;
            _hasher = hasher;
            _logger = logger;
            _db = db;
            _refreshTokenRepo = refreshTokenRepo;
            _configuration = configuration;
            _environment = environment;
            _emailService = emailService;
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
        /// Sends a password-reset email when an active account exists. Always returns the same
        /// message so callers cannot enumerate registered emails.
        /// </summary>
        public async Task<ForgotPasswordResponse> RequestPasswordResetAsync(ForgotPasswordRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var email = request.Email.Trim().ToLowerInvariant();

            // Look up by email only. Always return the same generic message to prevent enumeration.
            var user = await _userRepo.GetByEmailAsync(email);

            if (user != null && user.IsActive)
            {
                var rawToken = PasswordResetTokenHelper.GenerateRawToken();
                var tokenHash = PasswordResetTokenHelper.HashToken(rawToken);
                var expiresAt = DateTime.UtcNow.AddMinutes(PasswordResetTokenHelper.TokenValidityMinutes);

                await _userRepo.SetPasswordResetTokenAsync(user.Id, tokenHash, expiresAt);

                var frontendBase = _configuration["Frontend:BaseUrl"]?.TrimEnd('/') ?? string.Empty;
                var resetLink = $"{frontendBase}/reset-password?token={Uri.EscapeDataString(rawToken)}";
                var isDev = _environment.IsDevelopment();

                try
                {
                    await _emailService.SendPasswordResetEmailAsync(
                        user.Email ?? email,
                        user.Name,
                        resetLink,
                        isDev,
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send password reset email for user {UserId}", user.Id);
                    // Swallow — token is already persisted; user can retry.
                }
            }

            return new ForgotPasswordResponse { Message = PasswordResetGenericMessage };
        }

        /// <summary>
        /// Validates the reset token, sets the new password, and returns an access token for auto-login.
        /// </summary>
        public async Task<PasswordResetResponse> ResetPasswordWithTokenAsync(
            ResetPasswordRequest request, string ipAddress)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (request.NewPassword != request.ConfirmPassword)
                throw new ValidationException(
                    ErrorMessages.VALIDATION_FAILED,
                    new Dictionary<string, string[]>
                    {
                        ["confirmPassword"] = ["New password and confirm password do not match."]
                    });

            var tokenHash = PasswordResetTokenHelper.HashToken(request.Token.Trim());
            var user = await _userRepo.GetByPasswordResetTokenHashAsync(tokenHash);

            if (user == null || !user.IsActive)
                throw new ValidationException(
                    ErrorMessages.VALIDATION_FAILED,
                    new Dictionary<string, string[]>
                    {
                        ["token"] = ["This reset link is invalid or has already been used."]
                    });

            if (user.PasswordResetExpiresAt == null
                || user.PasswordResetExpiresAt.Value <= DateTime.UtcNow)
                throw new ValidationException(
                    ErrorMessages.VALIDATION_FAILED,
                    new Dictionary<string, string[]>
                    {
                        ["token"] = ["This reset link has expired. Please request a new one."]
                    });

            var newPasswordHash = _hasher.Hash(request.NewPassword);
            await _userRepo.SetPasswordAndClearResetTokenAsync(user.Id, newPasswordHash);

            _logger.LogInformation(
                "Password reset via token for user {UserId} from {IP}", user.Id, ipAddress);

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
