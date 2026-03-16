using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Shared.Jwt;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SocietyLedger.Infrastructure.Services
{

    public class TokenService : ITokenService
    {
        private readonly JwtSettings _settings;
        private readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();


        public TokenService(IOptions<JwtSettings> options) { _settings = options.Value; }


        /// <summary>
        /// Generates a JWT access token containing all fields required by the frontend:
        /// id, email, name, societyId, roles (array JSON), role (code), roleDisplayName.
        /// Also sets ClaimTypes.Role to the role code for ASP.NET Core policy evaluation.
        /// </summary>
        public string GenerateAccessToken(TokenClaims claims, out DateTime expiresAt)
        {
            var rolesJson = JsonSerializer.Serialize(new[]
            {
                new { id = claims.RoleId, code = claims.RoleCode, displayName = claims.RoleDisplayName }
            });

            var jwtClaims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, claims.UserId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("id", claims.UserPublicId.ToString()),
                new Claim("email", claims.Email),
                new Claim("name", claims.Name),
                new Claim("societyId", claims.SocietyPublicId.ToString()),
                new Claim("roles", rolesJson),
                new Claim("role", claims.RoleCode),
                new Claim("roleDisplayName", claims.RoleDisplayName),
                new Claim(ClaimTypes.Role, claims.RoleCode),
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            expiresAt = DateTime.UtcNow.AddMinutes(_settings.AccessTokenExpirationMinutes);
            var token = new JwtSecurityToken(
                issuer: _settings.Issuer,
                audience: _settings.Audience,
                claims: jwtClaims,
                expires: expiresAt,
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <inheritdoc/>
        public string GenerateAdminAccessToken(AdminTokenClaims claims, out DateTime expiresAt)
        {
            var jwtClaims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, claims.AdminId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("id",    claims.AdminPublicId.ToString()),
                new Claim("email", claims.Email),
                new Claim("name",  claims.Name),
                new Claim("role",  "super_admin"),
                new Claim(ClaimTypes.Role, "super_admin"),
            };

            var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            // Admin tokens are intentionally shorter-lived (60 min) for security.
            expiresAt = DateTime.UtcNow.AddMinutes(60);
            var token = new JwtSecurityToken(
                issuer:            _settings.Issuer,
                audience:          _settings.Audience,
                claims:            jwtClaims,
                expires:           expiresAt,
                signingCredentials: creds
            );
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <summary>
        /// Generates a secure random refresh token pair.
        /// </summary>
        public RefreshTokenPair GenerateRefreshToken()
        {
            var tokenBytes = new byte[64];
            _rng.GetBytes(tokenBytes);
            var token = Convert.ToBase64String(tokenBytes);
            var expires = DateTime.UtcNow.AddDays(_settings.RefreshTokenExpirationDays);
            return new RefreshTokenPair(token, expires);
        }

        /// <inheritdoc/>
        public string HashToken(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
