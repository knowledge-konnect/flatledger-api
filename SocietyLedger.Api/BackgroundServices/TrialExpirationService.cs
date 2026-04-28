using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using System.Text.Json;

namespace SocietyLedger.Api.BackgroundServices
{
    /// <summary>
    /// Background service that checks and expires trial subscriptions daily.
    /// </summary>
    public class TrialExpirationService : BackgroundService
    {
        private readonly ILogger<TrialExpirationService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly TimeSpan _retryDelay;

        public TrialExpirationService(
            ILogger<TrialExpirationService> logger,
            IServiceProvider serviceProvider,
            IConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _retryDelay = TimeSpan.FromMinutes(_configuration.GetValue<int>("BackgroundServices:TrialExpirationRetryMinutes", 5));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Trial expiration service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                // Align to next UTC midnight so the check always runs at a predictable time
                // regardless of when the process started or restarted.
                var now      = DateTime.UtcNow;
                var midnight = now.Date.AddDays(1);
                var delay    = midnight - now;
                try { await Task.Delay(delay, stoppingToken); }
                catch (OperationCanceledException) { break; }

                try
                {
                    await CheckExpiredTrialsAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in trial expiration check");
                    try { await Task.Delay(_retryDelay, stoppingToken); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }

        private async Task CheckExpiredTrialsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var subscriptionRepo = scope.ServiceProvider.GetRequiredService<ISubscriptionRepository>();
            var eventRepo        = scope.ServiceProvider.GetRequiredService<ISubscriptionEventRepository>();
            var db               = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var expiredTrials = (await subscriptionRepo.GetExpiredTrialsAsync()).ToList();
            if (expiredTrials.Count == 0)
                return;

            var now    = DateTime.UtcNow;
            var events = new List<SubscriptionEvent>(expiredTrials.Count);

            foreach (var subscription in expiredTrials)
            {
                subscription.Status    = SubscriptionStatusCodes.Expired;
                subscription.UpdatedAt = now;

                // Use JsonSerializer — never interpolate values into JSON strings.
                var eventMeta = JsonSerializer.Serialize(new { trial_end = subscription.TrialEnd });

                events.Add(new SubscriptionEvent
                {
                    Id             = Guid.NewGuid(),
                    UserId         = subscription.UserId,
                    SubscriptionId = subscription.Id,
                    EventType      = "trial_expired",
                    OldStatus      = SubscriptionStatusCodes.Trial,
                    NewStatus      = SubscriptionStatusCodes.Expired,
                    Metadata       = eventMeta,
                    CreatedAt      = now
                });
            }

            // Both writes commit atomically — if event insert fails the subscription
            // update is rolled back, preserving the Trial status for the next run.
            await using var tx = await db.Database.BeginTransactionAsync();
            await subscriptionRepo.BulkUpdateAsync(expiredTrials);
            await eventRepo.BulkCreateAsync(events);
            await tx.CommitAsync();

            _logger.LogInformation("Processed {Count} expired trials", expiredTrials.Count);
        }
    }
}
