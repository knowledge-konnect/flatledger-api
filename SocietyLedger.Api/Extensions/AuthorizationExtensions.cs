using Microsoft.AspNetCore.Authorization;
using SocietyLedger.Api.Authorization;
using System.Security.Claims;

namespace SocietyLedger.Api.Extensions;

public static class AuthorizationExtensions
{
    public static IServiceCollection AddApiAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy("ActiveSubscription", policy =>
                policy.Requirements.Add(new SubscriptionRequirement()));

            // Only JWTs issued by AdminAuthService carry the role:super_admin claim.
            options.AddPolicy("SuperAdmin", policy =>
                policy.RequireClaim(ClaimTypes.Role, "super_admin"));
        });

        services.AddScoped<IAuthorizationHandler, SubscriptionAuthorizationHandler>();

        return services;
    }
}
