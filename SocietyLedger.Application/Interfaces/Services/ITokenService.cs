using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SocietyLedger.Application.Interfaces.Services
{
    public interface ITokenService
    {
        /// <summary>
        /// Generates a JWT access token containing id, email, name, societyId,
        /// roles (array), role (code), and roleDisplayName claims.
        /// </summary>
        string GenerateAccessToken(TokenClaims claims, out DateTime expiresAt);

        /// <summary>
        /// Generates a short-lived JWT for the platform admin.
        /// Emits <c>role: super_admin</c> and <c>ClaimTypes.Role: super_admin</c>
        /// so the SuperAdmin authorization policy resolves correctly.
        /// </summary>
        string GenerateAdminAccessToken(AdminTokenClaims claims, out DateTime expiresAt);

        RefreshTokenPair GenerateRefreshToken();
        /// <summary>
        /// Returns a deterministic SHA-256 hex digest of a refresh token.
        /// Use this instead of a password hasher — refresh tokens are already
        /// high-entropy random values and need a fast, consistent lookup hash,
        /// not a slow salted hash (which produces a different output each call).
        /// </summary>
        string HashToken(string token);
    }

    /// <summary>
    /// All user and role data required to build the JWT payload.
    /// </summary>
    public record TokenClaims(
        long UserId,
        Guid UserPublicId,
        string Email,
        string Name,
        Guid SocietyPublicId,
        short RoleId,
        string RoleCode,
        string RoleDisplayName);

    public record RefreshTokenPair(string Token, DateTime ExpiresAt);

    /// <summary>
    /// Minimal claims needed for a platform admin JWT.
    /// Intentionally omits societyId, roleId, and roleDisplayName.
    /// </summary>
    public record AdminTokenClaims(
        long AdminId,
        Guid AdminPublicId,
        string Email,
        string Name);
}
