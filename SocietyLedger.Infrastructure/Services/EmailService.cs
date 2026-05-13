using Serilog;
using SocietyLedger.Application.Interfaces.Services;

namespace SocietyLedger.Infrastructure.Services
{
    /// <summary>
    /// Development email service. Logs reset links to console/logs.
    /// For production, replace with SendGrid, AWS SES, or similar provider.
    /// </summary>
    public class EmailService : IEmailService
    {
        public async Task SendPasswordResetEmailAsync(string userEmail, string userName, string resetLink, CancellationToken cancellationToken = default)
        {
            // In production, integrate with SendGrid, AWS SES, etc.
            // NEVER log the reset link in production — it contains a single-use security token.
            // Log only that the email was dispatched so operators can confirm delivery without
            // exposing the token in log aggregators (Datadog, Papertrail, etc.).
            Log.Information(
                "Password reset email dispatched to {Email} ({UserName}).",
                userEmail, userName);

            // Simulate async email send
            await Task.Delay(100, cancellationToken);
        }
    }
}
