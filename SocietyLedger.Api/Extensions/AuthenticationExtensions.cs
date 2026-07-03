using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using SocietyLedger.Shared.Jwt;
using System.Text;

namespace SocietyLedger.Api.Extensions;

public static class AuthenticationExtensions
{
    // NOTE: JwtSettings is read directly from IConfiguration (not IOptions) because
    // this runs during service registration, before the DI container is built —
    // IOptions isn't resolvable yet here. This is intentional, not duplication.
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var jwtConfig = configuration.GetSection("JwtSettings").Get<JwtSettings>() ?? new JwtSettings();
        var key = jwtConfig.Key ?? string.Empty;
        var issuer = jwtConfig.Issuer;
        var audience = jwtConfig.Audience;

        if (environment.IsProduction())
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    "ConnectionStrings:DefaultConnection is missing. Set the ConnectionStrings__DefaultConnection environment variable.");

            if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
                throw new InvalidOperationException("JwtSettings:Key must be set and at least 32 bytes in production.");
        }

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = issuer,
                ValidAudience = audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.Zero
            };
        });

        return services;
    }
}
