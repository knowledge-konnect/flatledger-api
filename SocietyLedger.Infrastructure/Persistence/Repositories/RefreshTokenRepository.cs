using Microsoft.EntityFrameworkCore;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Persistence.Entities;

namespace SocietyLedger.Infrastructure.Persistence.Repositories
{
    public class RefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly AppDbContext _db;
        public RefreshTokenRepository(AppDbContext db) => _db = db;

        public async Task<RefreshTokenEntity?> GetByHashAsync(string tokenHash)
        {
            var rt = await _db.refresh_tokens
                .Include(r => r.user).ThenInclude(u => u!.role)
                .Include(r => r.user).ThenInclude(u => u!.society)
                .FirstOrDefaultAsync(r => r.token_hash == tokenHash);

            if (rt == null) return null;

            return new RefreshTokenEntity
            {
                UserId              = rt.user_id,
                TokenHash           = rt.token_hash,
                JwtId               = rt.jwt_id ?? string.Empty,
                ExpiresAt           = rt.expires_at,
                CreatedAt           = rt.created_at,
                CreatedByIp         = rt.created_by_ip,
                IsRevoked           = rt.is_revoked,
                RevokedAt           = rt.revoked_at,
                ReplacedByTokenHash = rt.replaced_by_token_hash,
                User = rt.user == null ? null : new RefreshTokenUserInfo
                {
                    PublicId           = rt.user.public_id,
                    Email              = rt.user.email,
                    Name               = rt.user.name,
                    ForcePasswordChange = rt.user.force_password_change,
                    SocietyPublicId    = rt.user.society?.public_id ?? Guid.Empty,
                    SocietyName        = rt.user.society?.name,
                    RoleId             = rt.user.role?.id ?? 0,
                    RoleCode           = rt.user.role?.code,
                    RoleDisplayName    = rt.user.role?.display_name
                }
            };
        }

        public async Task AddAsync(RefreshTokenEntity token)
        {
            await _db.refresh_tokens.AddAsync(new refresh_token
            {
                user_id               = token.UserId,
                token_hash            = token.TokenHash,
                jwt_id                = token.JwtId,
                expires_at            = token.ExpiresAt,
                created_at            = token.CreatedAt,
                created_by_ip         = token.CreatedByIp,
                is_revoked            = token.IsRevoked,
                replaced_by_token_hash = token.ReplacedByTokenHash
            });
        }

        /// <summary>
        /// Revokes a token by its hash using a targeted UPDATE — no tracked SELECT needed.
        /// </summary>
        public async Task RevokeAsync(string tokenHash, DateTime revokedAt)
        {
            await _db.refresh_tokens
                .Where(r => r.token_hash == tokenHash)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.is_revoked, true)
                    .SetProperty(r => r.revoked_at, revokedAt));
        }

        public Task SaveChangesAsync() => _db.SaveChangesAsync();
    }
}
