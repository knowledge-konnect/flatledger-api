using Microsoft.AspNetCore.HttpOverrides;

namespace SocietyLedger.Api.Extensions;

public static class ForwardedHeadersExtensions
{
    // Trust Render's SSL-terminating load balancer so Request.Scheme is "https" and
    // X-Forwarded-For is populated correctly.
    // SECURITY: only clear the defaults when running on Render; in other environments
    // this would trust every hop in the X-Forwarded-For chain.
    public static IServiceCollection AddApiForwardedHeaders(this IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RENDER")))
            {
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
            }
        });

        return services;
    }
}
