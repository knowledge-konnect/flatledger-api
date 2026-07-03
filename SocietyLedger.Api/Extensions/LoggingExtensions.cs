using Serilog;
using Serilog.Events;
using Microsoft.Extensions.Hosting;

namespace SocietyLedger.Api.Extensions;

public static class LoggingExtensions
{
    private const string LogTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{CorrelationId}] {Message}{NewLine}{Exception}";

    public static IHostBuilder ConfigureApiSerilog(this IHostBuilder hostBuilder)
    {
        return hostBuilder.UseSerilog((ctx, lc) =>
        {
            var minLevel = ctx.HostingEnvironment.IsProduction() ? LogEventLevel.Warning : LogEventLevel.Information;
            lc
                .MinimumLevel.Is(minLevel)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName();

            if (ctx.HostingEnvironment.IsProduction())
            {
                lc.WriteTo.Async(a => a.Console(outputTemplate: LogTemplate));
            }
            else
            {
                lc.WriteTo.Async(a => a.Console(outputTemplate: LogTemplate))
                  .WriteTo.Async(a => a.File(
                      "Logs/SocietyLedger-.txt",
                      rollingInterval: RollingInterval.Day,
                      retainedFileCountLimit: 14,
                      fileSizeLimitBytes: 50_000_000,
                      rollOnFileSizeLimit: true,
                      outputTemplate: LogTemplate));
            }
        });
    }
}
