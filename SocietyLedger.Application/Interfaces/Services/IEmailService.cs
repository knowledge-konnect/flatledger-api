using SocietyLedger.Application.DTOs.Email;

namespace SocietyLedger.Application.Interfaces.Services
{
    public interface IEmailService
    {
        Task<bool> SendPasswordResetEmailAsync(string email, string token, string resetLink);
        Task<bool> SendPasswordResetEmailAsync(string email, string name, string resetLink, bool isDev, CancellationToken ct = default);
        Task<bool> SendContactUsNotificationAsync(ContactUsNotificationData data);
        Task<bool> SendContactUsNotificationAsync(string name, string email, string subject, string message, CancellationToken ct = default);
        Task<bool> SendSubscriptionExpiryReminderAsync(string email,string societyName,string planName,DateTime expiryDate,string renewUrl,string stage,CancellationToken ct = default);
        Task<bool> SendSubscriptionReminderEmailAsync(string email, string name, SubscriptionReminderData reminderData);
    }
}
