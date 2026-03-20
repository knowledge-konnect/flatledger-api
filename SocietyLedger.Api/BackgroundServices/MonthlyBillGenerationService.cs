using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SocietyLedger.Api.BackgroundServices
{
    /// <summary>
    /// Background service that checks daily if today is the 1st of the month and triggers monthly bill generation.
    /// Ensures bill generation runs only once per day.
    /// </summary>
    public class MonthlyBillGenerationService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MonthlyBillGenerationService> _logger;
        private readonly IConfiguration _configuration;
        private DateTime _lastRunDate = DateTime.MinValue;
        private readonly int _maxRetryAttempts;
        private readonly TimeSpan _retryDelay;

        public MonthlyBillGenerationService(IServiceProvider serviceProvider, ILogger<MonthlyBillGenerationService> logger, IConfiguration configuration)
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
                var now = DateTime.Now;
                if (now.Day == 1 && _lastRunDate.Date != now.Date)
                {
                    _logger.LogInformation("Monthly bill generation triggered on {Date}", now);
                    int attempt = 0;
                    bool success = false;
                    Exception? lastException = null;
                    while (attempt < _maxRetryAttempts && !success && !stoppingToken.IsCancellationRequested)
                    {
                        attempt++;
                        try
                        {
                            using (var scope = _serviceProvider.CreateScope())
                            {
                                // Use IBillingService to generate bills for all societies for the current month
                                var billingService = scope.ServiceProvider.GetRequiredService<SocietyLedger.Application.Interfaces.Services.IBillingService>();
                                var billingMonth = new DateTime(now.Year, now.Month, 1);
                                await billingService.GenerateMonthlyBillsAsync(billingMonth);
                            }
                            _logger.LogInformation("Monthly bill generation succeeded on attempt {Attempt}", attempt);
                            _lastRunDate = now.Date;
                            success = true;
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;
                            _logger.LogError(ex, "Attempt {Attempt} failed to generate monthly bills.", attempt);
                            if (attempt < _maxRetryAttempts)
                            {
                                _logger.LogInformation("Retrying in {RetryDelay} minutes...", _retryDelay.TotalMinutes);
                                try
                                {
                                    await Task.Delay(_retryDelay, stoppingToken);
                                }
                                catch (OperationCanceledException)
                                {
                                    // Service is stopping
                                    break;
                                }
                            }
                        }
                    }
                    if (!success && lastException != null)
                    {
                        _logger.LogCritical(lastException, "Monthly bill generation failed after {MaxRetryAttempts} attempts.", _maxRetryAttempts);
                    }
                }
                // Sleep until the next day (midnight)
                var tomorrow = now.Date.AddDays(1);
                var delay = tomorrow - now;
                await Task.Delay(delay, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Example interface for monthly bill generation service.
    /// Implement this in your application layer.
    /// </summary>
    public interface IMonthlyBillService
    {
        Task GenerateMonthlyBillsAsync(CancellationToken cancellationToken);
    }
}
