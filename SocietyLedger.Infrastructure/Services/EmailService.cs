using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SocietyLedger.Application.DTOs.Email;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Shared;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SocietyLedger.Infrastructure.Services
{
    public class EmailService : IEmailService
    {
        private readonly ILogger<EmailService> _logger;
        private readonly EmailSettings _settings;
        private readonly HttpClient _httpClient;

        public EmailService(ILogger<EmailService> logger, IOptions<EmailSettings> settings, HttpClient httpClient)
        {
            _logger = logger;
            _settings = settings.Value;
            _httpClient = httpClient;
            _httpClient.BaseAddress ??= new Uri("https://api.resend.com/");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ResendApiKey);
        }

        public async Task<bool> SendEmailAsync(string toEmail, string subject, string htmlContent)
        {
            try
            {
                var payload = new
                {
                    from = $"{_settings.FromName} <{_settings.FromAddress}>",
                    to = new[] { toEmail },
                    subject,
                    html = htmlContent
                };

                var response = await _httpClient.PostAsJsonAsync("emails", payload);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Resend API error sending to {Email}: {Status} {Error}", toEmail, response.StatusCode, error);
                    return false;
                }

                return true;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error sending email to {Email}", toEmail);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email to {Email}", toEmail);
                return false;
            }
        }

        public Task<bool> SendWelcomeEmailAsync(string email, string name)
            => SendEmailAsync(email, "Welcome to FlatLedger", GetWelcomeEmailHtml(name));

        public Task<bool> SendSubscriptionReminderEmailAsync(string email)
            => SendEmailAsync(email, "Your FlatLedger subscription needs attention", GetSubscriptionReminderEmailHtml());

        public Task<bool> SendPasswordResetEmailAsync(string email, string token, string resetLink)
            => SendEmailAsync(email, "Reset Your FlatLedger Password", GetPasswordResetEmailHtml(resetLink));

        public async Task<bool> SendPasswordResetEmailAsync(string email, string name, string resetLink, bool isDev, CancellationToken ct = default)
        {
            if (isDev)
            {
                _logger.LogInformation("[DEV] Password reset link for {Email}: {ResetLink}", email, resetLink);
                return true;
            }

            return await SendEmailAsync(email, "Reset Your FlatLedger Password", GetPasswordResetEmailHtml(resetLink));
        }

        public Task<bool> SendContactUsNotificationAsync(Application.DTOs.Email.ContactUsNotificationData data)
            => SendContactUsNotificationAsync(data.RecipientName, data.RecipientEmail, data.Subject, data.Message, data.Phone);

        public Task<bool> SendContactUsNotificationAsync(string name, string email, string subject, string message, CancellationToken ct = default)
            => SendContactUsNotificationAsync(name, email, subject, message, phone: null, ct);

        private Task<bool> SendContactUsNotificationAsync(string name, string email, string subject, string message, string? phone, CancellationToken ct = default)
        {
            var toAddress = string.IsNullOrWhiteSpace(_settings.ContactEmail)
                ? _settings.SupportEmail
                : _settings.ContactEmail;
            return SendEmailAsync(toAddress, $"New contact form submission: {subject}", GetContactUsNotificationHtml(name, email, subject, message, phone));
        }
        public Task<bool> SendSubscriptionExpiryReminderAsync(string email, string societyName, string planName, DateTime expiryDate, string renewUrl, string stage, CancellationToken ct = default)
    => SendEmailAsync(email,
        $"Your FlatLedger subscription expires on {expiryDate:dd MMM yyyy}",
        GetSubscriptionExpiryReminderHtml(societyName, planName, expiryDate, renewUrl, stage));
        public Task<bool> SendSubscriptionReminderEmailAsync(string email, string name, SubscriptionReminderData reminderData)
            => SendEmailAsync(email,
                $"Your FlatLedger subscription expires in {reminderData.DaysUntilExpiry} day(s)",
                GetSubscriptionReminderEmailHtml(name, reminderData));

        private string GetSubscriptionReminderEmailHtml(string name, SubscriptionReminderData data) => $@"
    <h2 style='margin-top:0;color:#111827;'>Hi {name}, your subscription is expiring soon</h2>
    <p style='color:#374151;line-height:1.7;'>
        The <strong>{data.CurrentPlan}</strong> plan for <strong>{data.SocietyName}</strong>
        expires on <strong>{data.ExpiryDate:dd MMM yyyy}</strong>
        ({data.DaysUntilExpiry} day(s) remaining).
        Renew now to avoid any interruption to billing and records.
    </p>
    <p style='text-align:center;margin:32px 0;'>
        <a href='{data.RenewSubscriptionLink}'
           style='background:#10B981;color:#ffffff !important;padding:14px 28px;
                  text-decoration:none;border-radius:10px;font-weight:600;display:inline-block;'>
            Renew Subscription
        </a>
    </p>
    <p style='font-size:13px;color:#6B7280;'>Questions? Reach us at {_settings.SupportEmail}.</p>";

        private string GetSubscriptionExpiryReminderHtml(
            string societyName, string planName, DateTime expiryDate, string renewUrl, string stage)
        {
            var urgencyColor = stage == "final" ? "#EF4444" : "#F59E0B";
            var urgencyLabel = stage switch
            {
                "final" => "⚠️ Last reminder — expires tomorrow",
                "week" => "Your subscription expires in 7 days",
                _ => "Your subscription is expiring soon"
            };

            return $@"
        <h2 style='margin-top:0;color:#111827;'>{urgencyLabel}</h2>
        <p style='color:#374151;line-height:1.7;'>
            The <strong>{planName}</strong> subscription for <strong>{societyName}</strong>
            expires on <strong>{expiryDate:dd MMM yyyy}</strong>.
            Renew now to avoid any interruption to billing and records.
        </p>
        <p style='text-align:center;margin:32px 0;'>
            <a href='{renewUrl}'
               style='background:{urgencyColor};color:#ffffff !important;padding:14px 28px;
                      text-decoration:none;border-radius:10px;font-weight:600;display:inline-block;'>
                Renew Subscription
            </a>
        </p>
        <p style='font-size:13px;color:#6B7280;'>
            Questions? Reach us at {_settings.SupportEmail}.
        </p>";
        }
        private string GetWelcomeEmailHtml(string name) => $@"
            <h2 style='margin-top:0;color:#111827;'>Welcome to FlatLedger, {name}!</h2>
            <p style='color:#374151;line-height:1.7;'>Your account is ready. You can now start managing your society's maintenance, bills, and payments.</p>
            <p style='text-align:center;margin:32px 0;'>
                <a href='{_settings.LoginUrl}' style='background:#10B981;color:#ffffff !important;padding:14px 28px;text-decoration:none;border-radius:10px;font-weight:600;display:inline-block;'>Go to Dashboard</a>
            </p>
            <p style='font-size:13px;color:#6B7280;'>Questions? Reach us at {_settings.SupportEmail}.</p>";

        private string GetSubscriptionReminderEmailHtml() => $@"
            <h2 style='margin-top:0;color:#111827;'>Your Subscription Needs Attention</h2>
            <p style='color:#374151;line-height:1.7;'>Your FlatLedger subscription has expired or is about to expire. Renew now to keep access to your society's records uninterrupted.</p>
            <p style='text-align:center;margin:32px 0;'>
                <a href='{_settings.LoginUrl}' style='background:#10B981;color:#ffffff !important;padding:14px 28px;text-decoration:none;border-radius:10px;font-weight:600;display:inline-block;'>Renew Subscription</a>
            </p>";

        private string GetPasswordResetEmailHtml(string resetLink)
        {
            var expiryMinutes = _settings.PasswordResetTokenExpiryMinutes;
            return $@"
            <h2 style='margin-top:0;color:#111827;'>Reset Your Password</h2>
            <p style='color:#374151;line-height:1.7;'>We received a request to reset your FlatLedger password.</p>
            <p style='text-align:center;margin:32px 0;'>
                <a href='{resetLink}' style='background:#10B981;color:#ffffff !important;padding:14px 28px;text-decoration:none;border-radius:10px;font-weight:600;display:inline-block;'>Reset Password</a>
            </p>
            <div style='background:#ECFDF5;border-left:4px solid #10B981;padding:16px;border-radius:8px;'>
                <strong>Security Notice</strong>
                <p style='margin:8px 0 0 0;'>This link expires in {expiryMinutes} minutes.</p>
            </div>
            <p style='margin-top:24px;color:#6B7280;'>If you didn't request this password reset, you can safely ignore this email.</p>
            <p style='font-size:13px;color:#6B7280;'>{resetLink}</p>";
        }

        private string GetContactUsNotificationHtml(string name, string email, string subject, string message, string? phone = null)
        {
            var phoneRow = !string.IsNullOrWhiteSpace(phone)
                ? $"<p style='color:#374151;'><strong>Phone:</strong> {phone}</p>"
                : string.Empty;
            return $@"
            <h2 style='margin-top:0;color:#111827;'>New Contact Form Submission</h2>
            <p style='color:#374151;'><strong>From:</strong> {name} ({email})</p>
            {phoneRow}
            <p style='color:#374151;'><strong>Subject:</strong> {subject}</p>
            <p style='color:#374151;line-height:1.7;white-space:pre-wrap;'>{message}</p>";
        }
    }
}