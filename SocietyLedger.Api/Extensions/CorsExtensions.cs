using Serilog;
using Microsoft.Extensions.Configuration;

namespace SocietyLedger.Api.Extensions;

public static class CorsExtensions
{
    public const string DefaultPolicyName = "DefaultCorsPolicy";

    public static IServiceCollection AddApiCors(this IServiceCollection services, IConfiguration configuration)
    {
        var allowedOrigins = configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

        if (allowedOrigins.Length == 0)
        {
            Log.Warning("AllowedOrigins configuration is empty — CORS will reject all cross-origin requests.");
        }

        services.AddCors(options =>
        {
            options.AddPolicy(DefaultPolicyName, policy =>
            {
                policy.WithOrigins(allowedOrigins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials(); // Required for httpOnly cookie support
            });
        });

        return services;
    }
}
