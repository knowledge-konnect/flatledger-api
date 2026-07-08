using SocietyLedger.Application.DTOs.Email;
using SocietyLedger.Application.Interfaces.Services;

namespace SocietyLedger.Infrastructure.Services
{
    public class EmailGatewayService : IEmailGatewayService
    {
        private readonly IEmailService _emailService;

        public EmailGatewayService(IEmailService emailService)
        {
            _emailService = emailService;
        }

        public Task<bool> SendGenericEmailAsync(string toEmail, string subject, string htmlContent, CancellationToken ct = default)
            => _emailService.SendEmailAsync(toEmail, subject, htmlContent);

        public Task<bool> SendPasswordResetEmailAsync(string email, string token, string resetLink)
            => _emailService.SendPasswordResetEmailAsync(email, token, resetLink);

        public Task<bool> SendPasswordResetEmailAsync(string email, string name, string resetLink, bool isDev, CancellationToken ct = default)
            => _emailService.SendPasswordResetEmailAsync(email, name, resetLink, isDev, ct);

        public Task<bool> SendContactUsNotificationAsync(string name, string email, string subject, string message, CancellationToken ct = default)
            => _emailService.SendContactUsNotificationAsync(name, email, subject, message, ct);

        public Task<bool> SendSubscriptionExpiryReminderAsync(string email, string societyName, string planName, DateTime expiryDate, string renewUrl, string stage, CancellationToken ct = default)
            => _emailService.SendSubscriptionExpiryReminderAsync(email, societyName, planName, expiryDate, renewUrl, stage, ct);

        public Task<bool> SendSubscriptionReminderEmailAsync(string email, string name, SubscriptionReminderData reminderData)
            => _emailService.SendSubscriptionReminderEmailAsync(email, name, reminderData);
    }
}
