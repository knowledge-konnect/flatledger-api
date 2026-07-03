using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using System;

namespace SocietyLedger.Api.Utilities
{
    public static class CookieHelper
    {
        public static CookieOptions GetRefreshTokenCookieOptions(IWebHostEnvironment env)
        {
            var isProduction = env.IsProduction();
            return new CookieOptions
            {
                HttpOnly = true,
                Path = "/api/auth",
                SameSite = isProduction ? SameSiteMode.None : SameSiteMode.Lax,
                Secure = isProduction,
                IsEssential = true
            };
        }
    }
}
