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
using SocietyLedger.Infrastructure.Persistence.Entities;
using SocietyLedger.Infrastructure.Persistence.Repositories;
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

        public AuthService(
            IUserRepository userRepo,
            IRoleRepository roleRepo,
            ISocietyRepository societyRepo,
            ITokenService tokenService,
            ISubscriptionService subscriptionService,
            PasswordHasher hasher,
            ILogger<AuthService> logger,
            AppDbContext db)
        {
            _userRepo = userRepo;
            _roleRepo = roleRepo;
            _societyRepo = societyRepo;
            _tokenService = tokenService;
            _subscriptionService = subscriptionService;
            _hasher = hasher;
            _logger = logger;
            _db = db;
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

            var refreshEntity = new refresh_token
            {
                user_id = user.Id,
                token_hash = _tokenService.HashToken(refreshPair.Token),
                jwt_id = Guid.NewGuid().ToString(),
                expires_at = refreshPair.ExpiresAt,
                created_at = DateTime.UtcNow,
                created_by_ip = ipAddress,
                is_revoked = false
            };

            var now = DateTime.UtcNow;

            // Direct UPDATE avoids a SELECT inside a transaction that can time out on pgBouncer.
            await _db.users
                .Where(u => u.id == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.last_login, now));

            _db.refresh_tokens.Add(refreshEntity);
            await _db.SaveChangesAsync();

            _logger.LogInformation("User {UserId} logged in successfully from IP {IP}", user.Id, ipAddress);

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
        /// Trial subscription creation is best-effort — a failure here does not roll back registration.
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

            var refreshEntity = new refresh_token
            {
                user_id = user.Id,
                token_hash = _tokenService.HashToken(refreshPair.Token),
                jwt_id = Guid.NewGuid().ToString(),
                expires_at = refreshPair.ExpiresAt,
                created_at = DateTime.UtcNow,
                created_by_ip = ipAddress,
                is_revoked = false
            };

            _db.refresh_tokens.Add(refreshEntity);
            await _db.SaveChangesAsync();

            // Trial subscription is best-effort — registration succeeds even if this fails.
            try
            {
                await _subscriptionService.CreateTrialSubscriptionAsync(user.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create trial subscription for user {UserId}", user.Id);
            }

            await tx.CommitAsync();

            _logger.LogInformation(
                "New user {UserId} registered new society {SocietyId} from {IP}",
                user.Id, society.Id, ipAddress);

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

            var rt = await _db.refresh_tokens
                .Include(r => r.user)
                .ThenInclude(u => u.role)
                .Include(r => r.user)
                .ThenInclude(u => u.society)
                .FirstOrDefaultAsync(r => r.token_hash == hashed);

            if (rt == null || rt.is_revoked || rt.expires_at <= DateTime.UtcNow)
                throw new AuthenticationException("Invalid or expired refresh token");

            var newPair = _tokenService.GenerateRefreshToken();

            var newRt = new refresh_token
            {
                user_id = rt.user_id,
                token_hash = _tokenService.HashToken(newPair.Token),
                jwt_id = Guid.NewGuid().ToString(),
                expires_at = newPair.ExpiresAt,
                created_at = DateTime.UtcNow,
                created_by_ip = ipAddress,
                is_revoked = false,
                replaced_by_token_hash = rt.token_hash
            };

            // Atomically revoke the old token and persist the new one.
            await using var transaction = await _db.Database.BeginTransactionAsync();

            try
            {
                rt.is_revoked = true;
                rt.revoked_at = DateTime.UtcNow;

                _db.refresh_tokens.Add(newRt);
                await _db.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            var refreshRole = rt.user?.role;
            var tokenClaimsRefresh = new TokenClaims(
                UserId: rt.user_id,
                UserPublicId: rt.user?.public_id ?? Guid.Empty,
                Email: rt.user?.email ?? string.Empty,
                Name: rt.user?.name ?? string.Empty,
                SocietyPublicId: rt.user?.society?.public_id ?? Guid.Empty,
                RoleId: (short)(refreshRole?.id ?? 0),
                RoleCode: refreshRole?.code ?? string.Empty,
                RoleDisplayName: refreshRole?.display_name ?? string.Empty);
            var accessToken = _tokenService.GenerateAccessToken(tokenClaimsRefresh, out var accessExpires);

            _logger.LogInformation("Refresh token rotated for user {UserId} from {Ip}", rt.user_id, ipAddress);

            return new LoginResponse
            {
                AccessToken = accessToken,
                AccessTokenExpiresAt = accessExpires,
                RefreshToken = newPair.Token,
                RefreshTokenExpiresAt = newPair.ExpiresAt,
                Roles = refreshRole != null
                    ? new[] { new RoleDto { Id = refreshRole.id, Code = refreshRole.code, DisplayName = refreshRole.display_name } }
                    : Enumerable.Empty<RoleDto>(),
                UserPublicId = rt.user?.public_id ?? Guid.Empty,
                UserName = rt.user?.name ?? string.Empty,
                Role = refreshRole?.code,
                RoleDisplayName = refreshRole?.display_name,
                SocietyPublicId = rt.user?.society?.public_id ?? Guid.Empty,
                SocietyName = rt.user?.society?.name ?? string.Empty,
                ForcePasswordChange = rt.user?.force_password_change ?? false
            };
        }

        /// <summary>
        /// Revokes a refresh token by its hashed value. Silently no-ops if the token is not found.
        /// </summary>
        public async Task RevokeRefreshTokenAsync(string token, string ipAddress)
        {
            var hashed = _tokenService.HashToken(token);
            var rt = await _db.refresh_tokens.FirstOrDefaultAsync(r => r.token_hash == hashed);
            if (rt == null)
                return;

            rt.is_revoked = true;
            rt.revoked_at = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            _logger.LogInformation("Refresh token revoked for user {UserId} from {Ip}", rt.user_id, ipAddress);
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
    }
}
