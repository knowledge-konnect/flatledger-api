using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using System.Linq;

namespace SocietyLedger.Api.Extensions;

public static class HealthCheckExtensions
{
    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var defaultConn = configuration.GetConnectionString("DefaultConnection");
        var healthChecks = services.AddHealthChecks();
        if (!string.IsNullOrWhiteSpace(defaultConn))
        {
            healthChecks.AddDbContextCheck<AppDbContext>("postgresql");
        }

        return services;
    }

    public static WebApplication MapApiHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                if (app.Environment.IsDevelopment())
                {
                    var details = new
                    {
                        status = report.Status.ToString(),
                        checks = report.Entries.ToDictionary(
                            kvp => kvp.Key,
                            kvp => new { status = kvp.Value.Status.ToString(), description = kvp.Value.Description })
                    };
                    await context.Response.WriteAsJsonAsync(details);
                }
                else
                {
                    await context.Response.WriteAsJsonAsync(new { status = report.Status.ToString() });
                }
            }
        });

        return app;
    }
}
