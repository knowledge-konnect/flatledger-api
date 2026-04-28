using System.Security.Claims;

namespace SocietyLedger.Api.Extensions
{
    /// <summary>
    /// Provides helper extensions for working with HttpContext.
    /// </summary>
    public static class HttpContextExtensions
    {
        /// <summary>
        /// Returns the real client IP address.
        /// Relies on UseForwardedHeaders middleware (configured in Program.cs) to have already
        /// resolved X-Forwarded-For into RemoteIpAddress — do NOT re-read the raw header here,
        /// as that would allow clients to spoof their IP and bypass rate limiting.
        /// </summary>
        public static string GetClientIp(this HttpContext ctx)
        {
            if (ctx == null)
                return "unknown";

            try
            {
                var address = ctx.Connection.RemoteIpAddress;
                if (address == null)
                    return "unknown";

                // Unwrap IPv4-mapped IPv6 addresses (e.g. ::ffff:192.168.1.10)
                if (address.IsIPv4MappedToIPv6)
                    address = address.MapToIPv4();

                var ip = address.ToString();

                // Normalize IPv6 loopback to IPv4 for consistency
                if (ip == "::1")
                    ip = "127.0.0.1";

                return ip;
            }
            catch
            {
                return "unknown";
            }
        }

        /// <summary>
        /// Gets the authenticated user's ID from JWT claims.
        /// </summary>
        public static long GetAuthenticatedUserId(this HttpContext ctx)
        {
            if (ctx?.User == null)
                return 0;

            var userIdClaim =
                ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? ctx.User.FindFirst("sub")?.Value;

            return long.TryParse(userIdClaim, out var userId)
                ? userId
                : 0;
        }

        /// <summary>
        /// Gets the authenticated user's ID from JWT claims (alias for GetAuthenticatedUserId).
        /// </summary>
        public static long GetUserId(this HttpContext ctx)
        {
            return ctx.GetAuthenticatedUserId();
        }

        /// <summary>
        /// Gets the role code (e.g. "society_admin" or "viewer") from the "role" JWT claim.
        /// Returns an empty string when no claim is present.
        /// </summary>
        public static string GetUserRoleCode(this HttpContext ctx)
        {
            return ctx?.User?.FindFirst("role")?.Value ?? string.Empty;
        }
    }
}
