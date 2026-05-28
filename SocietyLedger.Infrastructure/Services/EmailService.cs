using Serilog;
using SocietyLedger.Application.Interfaces.Services;

namespace SocietyLedger.Infrastructure.Services
{
    /// <summary>
    /// No-operation email service. Password resets use the mobile-verification flow
    /// (email + mobile required) and return the token directly in the API response.
    /// This service is intentionally a no-op; replace with SendGrid/SES when email
    /// delivery is required.
    /// </summary>
    public class EmailService : IEmailService
    {
        public Task SendPasswordResetEmailAsync(
            string userEmail,
            string userName,
            string resetLink,
            bool logResetLinkInDevelopment = false,
            CancellationToken cancellationToken = default)
        {
            Log.Warning(
                "Email delivery is not configured. Password reset requested for {Email} ({UserName}) — no email sent.",
                userEmail, userName);

            return Task.CompletedTask;
        }
    }
}
