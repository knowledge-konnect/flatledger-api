using Microsoft.OpenApi.Models;

namespace SocietyLedger.Api.Extensions;

public static class SwaggerExtensions
{
    public static IServiceCollection AddApiSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "SocietyLedger API", Version = "v1" });

            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter 'Bearer' followed by your token.\nExample: \"Bearer abc123\""
            });

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                    },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }

    // FIX: Swagger UI was previously mapped unconditionally, exposing the full API
    // schema at the root URL in production with no auth. Now gated to non-production.
    public static WebApplication UseApiSwagger(this WebApplication app)
    {
        if (!app.Environment.IsProduction())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "SocietyLedger API V1");
                c.RoutePrefix = "";
            });
        }

        return app;
    }
}
