using Microsoft.EntityFrameworkCore;
using Serilog;
using SocietyLedger.Infrastructure.Persistence.Contexts;

namespace SocietyLedger.Api.Extensions;

public static class DatabaseWarmupExtensions
{
    // Proactively warms the Supabase connection pool on startup. Free-tier Supabase
    // instances can take 60-90s to resume from idle; retrying here prevents the first
    // real request from timing out after a cold start.
    public static async Task WarmUpDatabaseAsync(this WebApplication app)
    {
        var settings = app.Configuration.GetSection("DatabaseWarmup").Get<DatabaseWarmupSettings>()
                       ?? new DatabaseWarmupSettings();

        for (int attempt = 1; attempt <= settings.MaxAttempts; attempt++)
        {
            try
            {
                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.ExecuteSqlRawAsync($"SET statement_timeout = '{settings.StatementTimeoutSeconds}s'; SELECT 1");
                Log.Information("Database warmup successful on attempt {Attempt}.", attempt);
                return;
            }
            catch (Exception ex)
            {
                Log.Warning("Database warmup attempt {Attempt}/{Max} failed: {Message}", attempt, settings.MaxAttempts, ex.Message);
                if (attempt < settings.MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(settings.RetryDelaySeconds));
                }
            }
        }

        Log.Warning("Database warmup failed after {Max} attempts.", settings.MaxAttempts);
    }
}
