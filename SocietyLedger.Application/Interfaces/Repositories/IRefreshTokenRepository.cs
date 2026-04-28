namespace SocietyLedger.Application.Interfaces.Repositories
{
    /// <summary>
    /// Repository for refresh token persistence. Keeps token lifecycle management
    /// out of AuthService so the service layer has no direct DbContext dependency
    /// for token operations.
    /// </summary>
    public interface IRefreshTokenRepository
    {
        /// <summary>Returns the refresh token (with user, role, and society navigation) by its hash.</summary>
        Task<RefreshTokenEntity?> GetByHashAsync(string tokenHash);

        /// <summary>Adds a new refresh token row.</summary>
        Task AddAsync(RefreshTokenEntity token);

        /// <summary>Persists pending changes (revocations, additions).</summary>
        Task SaveChangesAsync();

        /// <summary>Revokes a token by its hash using a targeted UPDATE. No-ops if not found.</summary>
        Task RevokeAsync(string tokenHash, DateTime revokedAt);
    }

    public sealed class RefreshTokenEntity
    {
        public long UserId { get; init; }
        public string TokenHash { get; init; } = string.Empty;
        public string JwtId { get; init; } = string.Empty;
        public DateTime ExpiresAt { get; init; }
        public DateTime CreatedAt { get; init; }
        public string? CreatedByIp { get; init; }
        public bool IsRevoked { get; set; }
        public DateTime? RevokedAt { get; set; }
        public string? ReplacedByTokenHash { get; init; }

        // Navigation — populated by GetByHashAsync
        public RefreshTokenUserInfo? User { get; init; }
    }

    public sealed class RefreshTokenUserInfo
    {
        public Guid PublicId { get; init; }
        public string? Email { get; init; }
        public string? Name { get; init; }
        public bool ForcePasswordChange { get; init; }
        public Guid SocietyPublicId { get; init; }
        public string? SocietyName { get; init; }
        public short RoleId { get; init; }
        public string? RoleCode { get; init; }
        public string? RoleDisplayName { get; init; }
    }
}
