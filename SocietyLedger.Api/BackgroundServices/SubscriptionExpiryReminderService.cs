using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using System.Text.Json;

namespace SocietyLedger.Api.BackgroundServices
{
    /// <summary>
    /// Daily background job that sends subscription expiry reminder emails to societies
    /// with Active subscriptions expiring in 7 days, 1 day, and on the expiry day.
    /// Uses SubscriptionEvent rows for deduplication — each reminder is sent at most once per stage.
    /// </summary>
    public class SubscriptionExpiryReminderService : BackgroundService
    {
        private readonly ILogger<SubscriptionExpiryReminderService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;

        private static readonly (string Stage, int DaysAhead)[] Stages =
        [
            ("7d", 7),
            ("1d", 1),
            ("0d", 0)
        ];

        public SubscriptionExpiryReminderService(
            ILogger<SubscriptionExpiryReminderService> logger,
            IServiceProvider serviceProvider,
            IConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Subscription expiry reminder service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                // Align to next UTC midnight — consistent scheduling regardless of restart time.
                var now = DateTime.UtcNow;
                var midnight = now.Date.AddDays(1);
                var delay = midnight - now;

                try { await Task.Delay(delay, stoppingToken); }
                catch (OperationCanceledException) { break; }

                try
                {
                    await ProcessRemindersAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in subscription expiry reminder check");
                }
            }
        }

        private async Task ProcessRemindersAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var subscriptionRepo = scope.ServiceProvider.GetRequiredService<ISubscriptionRepository>();
            var eventRepo = scope.ServiceProvider.GetRequiredService<ISubscriptionEventRepository>();
            var emailGatewayService = scope.ServiceProvider.GetRequiredService<IEmailGatewayService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var today = DateTime.UtcNow.Date;

            var frontendBase = _configuration["Frontend:BaseUrl"]?.TrimEnd('/') ?? string.Empty;
            var renewUrl = $"{frontendBase}/subscriptions";

            foreach (var (stage, daysAhead) in Stages)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var targetDate = today.AddDays(daysAhead);
                var fromDate = targetDate;
                var toDate = targetDate.AddDays(1);
                var eventType = $"expiry_reminder_{stage}";

                IEnumerable<Application.Interfaces.Repositories.SubscriptionExpiryInfo> expiring;
                try
                {
                    expiring = await subscriptionRepo.GetActiveSubscriptionsExpiringSoonAsync(fromDate, toDate);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to query expiring subscriptions for stage {Stage}", stage);
                    continue;
                }

                foreach (var info in expiring)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        // Deduplication: skip if this reminder was already sent for this subscription + stage.
                        var alreadySent = await eventRepo.ExistsAsync(info.SubscriptionId, eventType);
                        if (alreadySent)
                        {
                            _logger.LogDebug(
                                "Expiry reminder {Stage} already sent for subscription {SubscriptionId} — skipping",
                                stage, info.SubscriptionId);
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(info.UserEmail))
                        {
                            _logger.LogWarning(
                                "No email address for user {UserId} (subscription {SubscriptionId}) — skipping reminder",
                                info.UserId, info.SubscriptionId);
                            continue;
                        }

                        await emailGatewayService.SendSubscriptionExpiryReminderAsync(
                            info.UserEmail,
                            info.SocietyName,
                            info.PlanName,
                            info.ExpiryDate,
                            renewUrl,
                            stage,
                            cancellationToken);

                        // Record the event so this reminder is not sent again.
                        var eventMeta = JsonSerializer.Serialize(new
                        {
                            stage,
                            expiry_date = info.ExpiryDate,
                            days_ahead = daysAhead
                        });

                        await eventRepo.CreateAsync(new SubscriptionEvent
                        {
                            Id = Guid.NewGuid(),
                            UserId = info.UserId,
                            SocietyId = info.SocietyId,
                            SubscriptionId = info.SubscriptionId,
                            EventType = eventType,
                            OldStatus = SubscriptionStatusCodes.Active,
                            NewStatus = SubscriptionStatusCodes.Active,
                            Metadata = eventMeta,
                            CreatedAt = DateTime.UtcNow
                        });

                        _logger.LogInformation(
                            "Expiry reminder {Stage} sent for subscription {SubscriptionId} (society: {SocietyName})",
                            stage, info.SubscriptionId, info.SocietyName);
                    }
                    catch (Exception ex)
                    {
                        // Fault-isolated: log and continue with next subscription.
                        _logger.LogError(ex,
                            "Failed to process expiry reminder {Stage} for subscription {SubscriptionId}",
                            stage, info.SubscriptionId);
                    }
                }
            }
        }
    }
}
