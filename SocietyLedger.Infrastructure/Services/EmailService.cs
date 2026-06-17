using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SocietyLedger.Application.DTOs.Email;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Shared;

namespace SocietyLedger.Infrastructure.Services
{
    public class EmailService : IEmailService
    {
        private readonly HttpClient _httpClient;
        private readonly EmailSettings _emailSettings;
        private readonly ILogger<EmailService> _logger;

        public EmailService(
            IHttpClientFactory httpClientFactory,
            IOptions<EmailSettings> emailSettings,
            ILogger<EmailService> logger)
        {
            _httpClient = httpClientFactory.CreateClient("Resend");
            _emailSettings = emailSettings.Value;
            _logger = logger;
        }

        public async Task<bool> SendPasswordResetEmailAsync(string email, string resetToken, string resetUrl)
        {
            try
            {
                var subject = "Reset Your Password - FlatLedger";
                var htmlContent = GetPasswordResetEmailHtml(resetUrl);

                var response = await SendEmailAsync(email, subject, htmlContent);
                _logger.LogInformation("Password reset email sent to {Email}", email);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password reset email to {Email}", email);
                return false;
            }
        }

        public async Task<bool> SendWelcomeEmailAsync(string email, string name, WelcomeEmailData data)
        {
            try
            {
                var subject = $"Welcome to FlatLedger, {data.SocietyName}!";
                var htmlContent = GetWelcomeEmailHtml(data);

                var response = await SendEmailAsync(email, subject, htmlContent);
                _logger.LogInformation("Welcome email sent to {Email} for society {SocietyName}", email, data.SocietyName);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send welcome email to {Email}", email);
                return false;
            }
        }

        public async Task<bool> SendSubscriptionReminderEmailAsync(string email, string name, SubscriptionReminderData data)
        {
            try
            {
                var subject = data.DaysUntilExpiry switch
                {
                    0 => $"Your FlatLedger subscription has expired - {data.SocietyName}",
                    1 => $"Your FlatLedger subscription expires tomorrow - {data.SocietyName}",
                    _ => $"Your FlatLedger subscription expires in {data.DaysUntilExpiry} days - {data.SocietyName}"
                };

                var htmlContent = GetSubscriptionReminderEmailHtml(data);

                var response = await SendEmailAsync(email, subject, htmlContent);
                _logger.LogInformation("Subscription reminder email sent to {Email} for society {SocietyName}", email, data.SocietyName);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send subscription reminder email to {Email}", email);
                return false;
            }
        }

        public async Task<bool> SendContactUsNotificationAsync(ContactUsNotificationData data)
        {
            try
            {
                var subject = $"Contact Us Form Submission: {data.Subject}";
                var htmlContent = GetContactUsNotificationHtml(data);

                var response = await SendEmailAsync(_emailSettings.SupportEmail, subject, htmlContent);
                _logger.LogInformation("Contact Us notification sent to support for submission from {Email}", data.SubmitterEmail);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send contact us notification for {Email}", data.SubmitterEmail);
                return false;
            }
        }

        private async Task<bool> SendEmailAsync(string toEmail, string subject, string htmlContent)
        {
            try
            {
                var request = new
                {
                    from = $"{_emailSettings.SenderName} <{_emailSettings.SenderEmail}>",
                    to = toEmail,
                    subject = subject,
                    html = htmlContent
                };

                var response = await _httpClient.PostAsJsonAsync("", request);
                response.EnsureSuccessStatusCode();

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

        private string BuildEmailTemplate(string title, string content)
        {
            return $@"
                    <!DOCTYPE html>
                    <html>
                    <head>
                    <meta charset='UTF-8'>
                    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                    <title>{title}</title>
                    </head>
                    <body style='margin:0;padding:0;background:#F8FAFC;font-family:Segoe UI,Arial,sans-serif;'>

                    <table width='100%' cellpadding='0' cellspacing='0' style='padding:40px 20px;background:#F8FAFC;'>
                    <tr>
                    <td align='center'>

                    <table width='600' cellpadding='0' cellspacing='0'
                    style='background:#ffffff;border-radius:16px;overflow:hidden;border:1px solid #E5E7EB;'>

                    <tr>
                    <td align='center'
                    style='background:linear-gradient(135deg,#10B981,#059669);padding:32px;'>

                    <h1 style='margin:0;color:#ffffff;font-size:32px;font-weight:700;'>
                    FlatLedger
                    </h1>

                    <p style='margin:8px 0 0 0;color:#D1FAE5;font-size:14px;'>
                    Society Management Made Simple
                    </p>

                    </td>
                    </tr>

                    <tr>
                    <td style='padding:40px;'>
                    {content}
                    </td>
                    </tr>

                    <tr>
                    <td style='padding:24px;background:#F9FAFB;border-top:1px solid #E5E7EB;'>

                    <p style='margin:0;font-size:14px;color:#374151;font-weight:600;'>
                    FlatLedger
                    </p>

                    <p style='margin:6px 0;color:#6B7280;font-size:13px;'>
                    Simple accounting for apartment societies.
                    </p>

                    <p style='margin:6px 0;color:#6B7280;font-size:13px;'>
                    🌐 https://flatledger.in
                    </p>

                    <p style='margin:6px 0;color:#6B7280;font-size:13px;'>
                    📧 support@flatledger.in
                    </p>

                    <p style='margin-top:16px;color:#9CA3AF;font-size:12px;'>
                    © {DateTime.Now.Year} FlatLedger. All rights reserved.
                    </p>

                    </td>
                    </tr>

                    </table>

                    </td>
                    </tr>
                    </table>

                    </body>
                    </html>";
        }
        private string GetPasswordResetEmailHtml(string resetLink)
        {
            var expiryMinutes = _emailSettings.PasswordResetTokenExpiryMinutes;

            var content = $@"
            <h2 style='margin-top:0;color:#111827;'>Reset Your Password</h2>

            <p style='color:#374151;line-height:1.7;'>
            We received a request to reset your FlatLedger password.
            </p>

            <p style='text-align:center;margin:32px 0;'>
            <a href='{resetLink}'
            style='background:#10B981;
            color:#ffffff !important;
            padding:14px 28px;
            text-decoration:none;
            border-radius:10px;
            font-weight:600;
            display:inline-block;'>
            Reset Password
            </a>
            </p>

            <div style='background:#ECFDF5;
            border-left:4px solid #10B981;
            padding:16px;
            border-radius:8px;'>

            <strong>Security Notice</strong>
            <p style='margin:8px 0 0 0;'>
            This link expires in {expiryMinutes} minutes.
            </p>

            </div>

            <p style='margin-top:24px;color:#6B7280;'>
            If you didn't request this password reset, you can safely ignore this email.
            </p>

            <p style='font-size:13px;color:#6B7280;'>
            {resetLink}
            </p>";

            return BuildEmailTemplate("Reset Your Password", content);
        }
        private string GetWelcomeEmailHtml(WelcomeEmailData data)
        {
            var content = $@"
                    <h2 style='margin-top:0;color:#111827;'>
                    🎉 Welcome to FlatLedger
                    </h2>

                    <p>
                    Hello <strong>{data.AdminName}</strong>,
                    </p>

                    <p>
                    Your society <strong>{data.SocietyName}</strong> has been successfully registered.
                    </p>

                    <div style='background:#ECFDF5;
                    padding:20px;
                    border-radius:10px;
                    margin:20px 0;'>

                    <p><strong>Society:</strong> {data.SocietyName}</p>
                    <p><strong>Plan:</strong> {data.CurrentPlanName}</p>

                    </div>

                    <p style='text-align:center;margin:30px 0;'>

                    <a href='{data.LoginUrl}'
                    style='background:#10B981;
                    color:white !important;
                    padding:14px 28px;
                    border-radius:10px;
                    text-decoration:none;
                    font-weight:600;'>

                    Go To Dashboard

                    </a>

                    </p>

                    <h3>Getting Started</h3>

                    <ul>
                    <li>Add Flats</li>
                    <li>Configure Maintenance</li>
                    <li>Record Expenses</li>
                    <li>Generate Reports</li>
                    </ul>";

            return BuildEmailTemplate("Welcome to FlatLedger", content);
        }

        private string GetSubscriptionReminderEmailHtml(SubscriptionReminderData data)
        {
            var color = data.DaysUntilExpiry <= 1
                ? "#DC2626"
                : "#10B981";

            var content = $@"
                    <h2 style='color:{color};margin-top:0;'>
                    Subscription Reminder
                    </h2>

                    <p>
                    Hello {data.RecipientName},
                    </p>

                    <p>
                    Your subscription for <strong>{data.SocietyName}</strong> will expire in
                    <strong>{data.DaysUntilExpiry}</strong> day(s).
                    </p>

                    <div style='background:#F9FAFB;
                    padding:20px;
                    border-radius:10px;
                    border:1px solid #E5E7EB;'>

                    <p><strong>Plan:</strong> {data.CurrentPlan}</p>
                    <p><strong>Expiry:</strong> {data.ExpiryDate:dd MMM yyyy}</p>

                    </div>

                    <p style='text-align:center;margin:30px 0;'>

                    <a href='{data.RenewSubscriptionLink}'
                    style='background:{color};
                    color:white !important;
                    padding:14px 28px;
                    border-radius:10px;
                    text-decoration:none;
                    font-weight:600;'>

                    Renew Subscription

                    </a>

                    </p>";

            return BuildEmailTemplate("Subscription Reminder", content);
        }
        private string GetContactUsNotificationHtml(ContactUsNotificationData data)
        {
            var content = $@"
                    <h2 style='margin-top:0;'>
                    New Contact Request
                    </h2>

                    <div style='background:#F9FAFB;
                    padding:20px;
                    border-radius:10px;
                    border:1px solid #E5E7EB;'>

                    <p><strong>Name:</strong> {data.RecipientName}</p>
                    <p><strong>Email:</strong> {data.RecipientEmail}</p>
                    <p><strong>Subject:</strong> {data.Subject}</p>

                    </div>

                    <h3>Message</h3>

                    <div style='background:#F3F4F6;
                    padding:20px;
                    border-radius:10px;'>

                    {data.Message}

                    </div>";

            return BuildEmailTemplate("Contact Request", content);
        }
        
    }
}