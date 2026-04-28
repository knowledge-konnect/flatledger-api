using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SocietyLedger.Application.Interfaces.Services;

namespace SocietyLedger.Api.BackgroundServices
{
    /// <summary>
    /// Background service that triggers monthly bill generation on the 1st of each month.
    /// Uses the DB (via <see cref="IBillingService"/> idempotency) as the authoritative source for
    /// whether bills have already been generated for the current period. The in-process
    /// <c>_lastRunDate</c> guard was removed in favour of a DB check so that a process restart
    /// on the 1st does not skip billing — <c>GenerateMonthlyBillsAsync</c> skips flats that
    /// already have a bill for the period, making re-runs safe.
    /// </summary>
    public class MonthlyBillGenerationService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MonthlyBillGenerationService> _logger;
        private readonly IConfiguration _configuration;
        private readonly int _maxRetryAttempts;
        private readonly TimeSpan _retryDelay;

        public MonthlyBillGenerationService(
            IServiceProvider serviceProvider,
            ILogger<MonthlyBillGenerationService> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _configuration = configuration;
            _maxRetryAttempts = _configuration.GetValue<int>("BackgroundServices:MonthlyBillMaxRetryAttempts", 3);
            _retryDelay = TimeSpan.FromMinutes(_configuration.GetValue<int>("BackgroundServices:MonthlyBillRetryMinutes", 5));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;

                if (now.Day == 1)
                {
                    // Idempotency is fully handled inside GenerateMonthlyBillsAsync, which skips
                    // any flat that already has a bill for the period. Safe to call on every
                    // restart that lands on the 1st without double-billing.
                    var currentPeriod = now.ToString("yyyy-MM");
                    _logger.LogInformation("Monthly bill generation triggered. Period={Period}", currentPeriod);
                    await RunWithRetryAsync(now, stoppingToken);
                }

                // Sleep until next UTC midnight
                var tomorrow = now.Date.AddDays(1);
                var delay = tomorrow - now;
                try { await Task.Delay(delay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task RunWithRetryAsync(DateTime now, CancellationToken stoppingToken)
        {
            int attempt = 0;
            Exception? lastException = null;

            while (attempt < _maxRetryAttempts && !stoppingToken.IsCancellationRequested)
            {
                attempt++;
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var billingService = scope.ServiceProvider.GetRequiredService<IBillingService>();
                    var billingMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    await billingService.GenerateMonthlyBillsAsync(billingMonth);

                    _logger.LogInformation(
                        "Monthly bill generation succeeded. Period={Period} Attempt={Attempt}",
                        billingMonth.ToString("yyyy-MM"), attempt);
                    return;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.LogError(ex,
                        "Monthly bill generation attempt failed. Period={Period} Attempt={Attempt}/{Max}",
                        now.ToString("yyyy-MM"), attempt, _maxRetryAttempts);

                    if (attempt < _maxRetryAttempts)
                    {
                        try { await Task.Delay(_retryDelay, stoppingToken); }
                        catch (OperationCanceledException) { return; }
                    }
                }
            }

            _logger.LogCritical(lastException,
                "Monthly bill generation failed after all retries. Period={Period} Attempts={Attempts}",
                now.ToString("yyyy-MM"), _maxRetryAttempts);
        }

    }
}
