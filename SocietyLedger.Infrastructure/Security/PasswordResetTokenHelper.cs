using System.Security.Cryptography;
using System.Text;

namespace SocietyLedger.Infrastructure.Security
{
    /// <summary>Generates URL-safe reset tokens and SHA-256 hashes for storage (never store raw tokens).</summary>
    public static class PasswordResetTokenHelper
    {
        public const int TokenValidityMinutes = 30;

        public static string GenerateRawToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        public static string HashToken(string rawToken)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
            return Convert.ToHexString(hash);
        }
    }
}
