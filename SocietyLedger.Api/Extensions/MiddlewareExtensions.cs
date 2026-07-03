using Serilog;
using Serilog.Events;
using System.Security.Claims;

namespace SocietyLedger.Api.Extensions;

public static class MiddlewareExtensions
{
    // FIX: X-XSS-Protection removed — modern browsers ignore it.
    public static WebApplication UseApiSecurityHeaders(this WebApplication app)
    {
        app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            ctx.Response.Headers.Append("X-Frame-Options", "DENY");
            ctx.Response.Headers.Append("Referrer-Policy", "no-referrer");
            if (!ctx.Request.Path.StartsWithSegments("/swagger"))
            {
                ctx.Response.Headers.Append("Content-Security-Policy", "default-src 'none'");
            }
            await next();
        });

        return app;
    }

    public static WebApplication UseApiRequestLogging(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0}ms";
            options.GetLevel = (ctx, elapsed, ex) =>
            {
                if (ctx.Request.Path.StartsWithSegments("/health") ||
                    ctx.Request.Path.StartsWithSegments("/swagger"))
                    return LogEventLevel.Verbose;
                return ex != null || ctx.Response.StatusCode >= 500 ? LogEventLevel.Error :
                       ctx.Response.StatusCode >= 400 ? LogEventLevel.Warning :
                       elapsed > 1000 ? LogEventLevel.Warning :
                       LogEventLevel.Information;
            };
            options.EnrichDiagnosticContext = (diagCtx, httpCtx) =>
            {
                diagCtx.Set("UserId", httpCtx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous");
                diagCtx.Set("CorrelationId", httpCtx.Request.Headers["X-Correlation-ID"].FirstOrDefault() ?? httpCtx.TraceIdentifier);
            };
        });

        return app;
    }
}
