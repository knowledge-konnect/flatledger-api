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
            // In development, log the reset link. In production, integrate with SendGrid, AWS SES, etc.
            Log.Information(
                "Password reset email sent to {Email} ({UserName}). Reset link: {ResetLink}",
                userEmail, userName, resetLink);

            // Simulate async email send
            await Task.Delay(100, cancellationToken);
        }
    }
}
