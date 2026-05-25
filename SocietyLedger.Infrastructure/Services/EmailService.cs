using Serilog;
using SocietyLedger.Application.Interfaces.Services;

namespace SocietyLedger.Infrastructure.Services
{
    /// <summary>
    /// Development email service. Logs reset links to console/logs in Development only.
    /// For production, replace with SendGrid, AWS SES, or similar provider.
    /// </summary>
    public class EmailService : IEmailService
    {
        public async Task SendPasswordResetEmailAsync(
            string userEmail,
            string userName,
            string resetLink,
            bool logResetLinkInDevelopment = false,
            CancellationToken cancellationToken = default)
        {
            Log.Information(
                "Password reset email dispatched to {Email} ({UserName}).",
                userEmail, userName);

            if (logResetLinkInDevelopment)
            {
                Log.Warning(
                    "DEV ONLY — password reset link for {Email}: {ResetLink}",
                    userEmail, resetLink);
            }

            // Simulate async email send; integrate SendGrid/SES in production.
            await Task.Delay(100, cancellationToken);
        }
    }
}
