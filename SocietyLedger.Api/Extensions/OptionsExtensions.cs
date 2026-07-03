using SocietyLedger.Shared.Jwt;
using SocietyLedger.Shared;

namespace SocietyLedger.Api.Extensions;

public static class OptionsExtensions
{
    // ASSUMPTION: EmailSettings, RazorpaySettings, DatabaseSettings live in
    // SocietyLedger.Shared alongside JwtSettings. Adjust the using directive if not.
    public static IServiceCollection AddApiOptions(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var jwtBuilder = services.AddOptions<JwtSettings>()
            .Bind(configuration.GetRequiredSection("JwtSettings"))
            .ValidateDataAnnotations();
        if (environment.IsProduction()) jwtBuilder.ValidateOnStart();

        var emailBuilder = services.AddOptions<EmailSettings>()
            .Bind(configuration.GetRequiredSection("Email"))
            .ValidateDataAnnotations();
        if (environment.IsProduction()) emailBuilder.ValidateOnStart();

        var razorpayBuilder = services.AddOptions<RazorpaySettings>()
            .Bind(configuration.GetRequiredSection("Razorpay"))
            .ValidateDataAnnotations();
        if (environment.IsProduction()) razorpayBuilder.ValidateOnStart();

        var dbBuilder = services.AddOptions<DatabaseSettings>()
            .Bind(configuration.GetRequiredSection("ConnectionStrings"))
            .ValidateDataAnnotations();
        if (environment.IsProduction()) dbBuilder.ValidateOnStart();

        return services;
    }
}
