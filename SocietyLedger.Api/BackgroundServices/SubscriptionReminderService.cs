using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SocietyLedger.Application.DTOs.Email;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Shared;
using Microsoft.EntityFrameworkCore;

namespace SocietyLedger.Api.BackgroundServices
{
    /// <summary>
    /// Background service that sends subscription expiry reminder emails:
    /// - 7 days before expiry
    /// - 1 day before expiry
    /// - On expiry date
    /// Prevents duplicate reminders by tracking sent notifications.
    /// </summary>
    public class SubscriptionReminderService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SubscriptionReminderService> _logger;
        private readonly EmailSettings _emailSettings;
        private DateTime _lastRunDate = DateTime.MinValue;

        public SubscriptionReminderService(
            IServiceProvider serviceProvider,
            ILogger<SubscriptionReminderService> logger,
            IOptions<EmailSettings> emailSettings)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _emailSettings = emailSettings.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("SubscriptionReminderService started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    
                    // Run once per day
                    if (_lastRunDate.Date != now.Date)
                    {
                        _logger.LogInformation("Running subscription reminder check for {Date}", now.Date);
                        await CheckAndSendRemindersAsync(now);
                        _lastRunDate = now.Date;
                    }

                    // Sleep until tomorrow
                    var tomorrow = now.Date.AddDays(1);
                    var delay = tomorrow - now;
                    await Task.Delay(delay, stoppingToken);
                }
                catch (Exception ex) when (ex is not TaskCanceledException)
                {
                    _logger.LogError(ex, "Error in SubscriptionReminderService");
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }

            _logger.LogInformation("SubscriptionReminderService stopped.");
        }

        private async Task CheckAndSendRemindersAsync(DateTime currentDate)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            try
            {
                // Find subscriptions expiring in 7, 1, or 0 days
                var reminderDates = new[]
                {
                    currentDate.Date.AddDays(7),  // 7 days from now
                    currentDate.Date.AddDays(1),  // 1 day from now
                    currentDate.Date              // Today
                };

                foreach (var reminderDate in reminderDates)
                {
                    await ProcessRemindersForDateAsync(dbContext, emailService, reminderDate, currentDate);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking subscription reminders");
            }
        }

        private async Task ProcessRemindersForDateAsync(
            AppDbContext dbContext,
            IEmailService emailService,
            DateTime expiryDateToCheck,
            DateTime currentDate)
        {
            var daysUntilExpiry = (expiryDateToCheck - currentDate.Date).Days;

            // Get subscriptions expiring on the target date
            var expiringSubscriptions = await dbContext.subscriptions
                .Include(s => s.user)
                .ThenInclude(u => u.society)
                .Include(s => s.plan)
                .Where(s => s.status == "active" || s.status == "trial")
                .Where(s => s.current_period_end.HasValue && s.current_period_end.Value.Date == expiryDateToCheck.Date)
                .ToListAsync();

            _logger.LogInformation("Found {Count} subscriptions expiring on {ExpiryDate} ({Days} days from now)", 
                expiringSubscriptions.Count, expiryDateToCheck.Date, daysUntilExpiry);

            foreach (var subscription in expiringSubscriptions)
            {
                try
                {
                    // Check if reminder already sent today for this subscription
                    var reminderAlreadySent = await dbContext.email_notification_logs
                        .AnyAsync(log =>
                            log.user_id == subscription.user_id &&
                            log.notification_type == "subscription_reminder" &&
                            log.sent_at.Date == currentDate.Date &&
                            log.metadata != null && log.metadata.Contains($"\"days_until_expiry\":{daysUntilExpiry}"));

                    if (reminderAlreadySent)
                    {
                        _logger.LogDebug("Reminder already sent today for subscription {SubscriptionId} (days: {Days})", 
                            subscription.id, daysUntilExpiry);
                        continue;
                    }

                    // Send reminder email
                    if (subscription.user?.email != null && subscription.user?.society != null)
                    {
                        var frontendUrl = _emailSettings.FrontendUrl.TrimEnd('/');
                        var reminderData = new SubscriptionReminderData
                        {
                            RecipientName = subscription.user.name,
                            RecipientEmail = subscription.user.email,
                            SocietyName = subscription.user.society.name,
                            CurrentPlan = subscription.plan?.name ?? "Unknown Plan",
                            ExpiryDate = expiryDateToCheck,
                            RenewSubscriptionLink = $"{frontendUrl}/subscription/renew",
                            DaysUntilExpiry = daysUntilExpiry
                        };

                        var emailSent = await emailService.SendSubscriptionReminderEmailAsync(
                            subscription.user.email,
                            subscription.user.name,
                            reminderData);

                        if (emailSent)
                        {
                            // Log the sent notification
                            var notificationLog = new Infrastructure.Persistence.Entities.email_notification_log
                            {
                                notification_type = "subscription_reminder",
                                recipient_email = subscription.user.email,
                                recipient_name = subscription.user.name,
                                subject = $"Subscription Reminder - {daysUntilExpiry} days",
                                sent_at = DateTime.UtcNow,
                                sent_by_system = true,
                                status = "sent",
                                society_id = subscription.user.society_id,
                                user_id = subscription.user_id,
                                metadata = $"{{\"days_until_expiry\":{daysUntilExpiry},\"subscription_id\":\"{subscription.id}\"}}"
                            };

                            await dbContext.email_notification_logs.AddAsync(notificationLog);
                            await dbContext.SaveChangesAsync();

                            _logger.LogInformation("Subscription reminder email sent to {Email} for society {SocietyName} (days: {Days})",
                                subscription.user.email, subscription.user.society.name, daysUntilExpiry);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending reminder for subscription {SubscriptionId}", subscription.id);
                }
            }
        }
    }
}