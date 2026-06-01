using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Resend;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Infrastructure.Services.Templates;

namespace SocietyLedger.Infrastructure.Services
{
    public class EmailService : IEmailService
    {
        private readonly IResend _resend;
        private readonly EmailSettings _settings;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IResend resend, IOptions<EmailSettings> settings, ILogger<EmailService> logger)
        {
            _resend = resend;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task SendPasswordResetEmailAsync(
            string userEmail,
            string userName,
            string resetLink,
            bool logResetLinkInDevelopment = false,
            CancellationToken cancellationToken = default)
        {
            if (logResetLinkInDevelopment)
                _logger.LogInformation("[DEV] Password reset link for {Email}: {ResetLink}", userEmail, resetLink);

            var expiresAt = DateTime.UtcNow.AddMinutes(30);
            var html = EmailTemplates.PasswordReset(userName, resetLink, expiresAt, _settings.SupportEmail, _settings.SupportPhone);

            await SendAsync(
                to: userEmail,
                subject: "Reset your FlatLedger password",
                html: html,
                context: $"password reset for {userEmail}",
                cancellationToken: cancellationToken);
        }

        public async Task SendWelcomeEmailAsync(
            string userEmail,
            string adminName,
            string societyName,
            string planName,
            DateTime trialOrExpiryEnd,
            string loginUrl,
            CancellationToken cancellationToken = default)
        {
            var html = EmailTemplates.Welcome(
                adminName,
                societyName,
                planName,
                trialOrExpiryEnd.ToString("dd MMM yyyy"),
                loginUrl,
                _settings.SupportEmail,
                _settings.SupportPhone);

            await SendAsync(
                to: userEmail,
                subject: $"Welcome to FlatLedger — {societyName} is ready!",
                html: html,
                context: $"welcome email for {userEmail}",
                cancellationToken: cancellationToken);
        }

        public async Task SendSubscriptionExpiryReminderAsync(
            string userEmail,
            string societyName,
            string planName,
            DateTime expiryDate,
            string renewUrl,
            string stage,
            CancellationToken cancellationToken = default)
        {
            var subjectPrefix = stage switch
            {
                "0d" => "Action Required: Your FlatLedger subscription expires today",
                "1d" => "Reminder: Your FlatLedger subscription expires tomorrow",
                _ => "Reminder: Your FlatLedger subscription expires in 7 days"
            };

            var html = EmailTemplates.SubscriptionExpiryReminder(
                societyName,
                planName,
                expiryDate.ToString("dd MMM yyyy"),
                renewUrl,
                _settings.SupportEmail,
                _settings.SupportPhone,
                stage);

            await SendAsync(
                to: userEmail,
                subject: subjectPrefix,
                html: html,
                context: $"expiry reminder ({stage}) for {userEmail}",
                cancellationToken: cancellationToken);
        }

        public async Task SendContactUsNotificationAsync(
            string senderName,
            string senderEmail,
            string subject,
            string message,
            CancellationToken cancellationToken = default)
        {
            var html = EmailTemplates.ContactUsNotification(senderName, senderEmail, subject, message, _settings.SupportEmail, _settings.SupportPhone);

            // Use dedicated ContactEmail when configured; fall back to SupportEmail.
            var destination = string.IsNullOrWhiteSpace(_settings.ContactEmail)
                ? _settings.SupportEmail
                : _settings.ContactEmail;

            await SendAsync(
                to: destination,
                subject: $"New Contact Form Submission — {subject}",
                html: html,
                context: $"contact-us from {senderEmail}",
                cancellationToken: cancellationToken);
        }

        private async Task SendAsync(
            string to,
            string subject,
            string html,
            string context,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_settings.ResendApiKey))
            {
                _logger.LogWarning("Email not sent ({Context}): ResendApiKey is not configured. Set Email__ResendApiKey environment variable.", context);
                return;
            }

            try
            {
                var message = new EmailMessage();
                message.From = $"{_settings.FromName} <{_settings.FromAddress}>";
                message.To.Add(to);
                message.Subject = subject;
                message.HtmlBody = html;

                await _resend.EmailSendAsync(message, cancellationToken);
                _logger.LogInformation("Email sent ({Context})", context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email ({Context})", context);
                throw;
            }
        }
    }
}
