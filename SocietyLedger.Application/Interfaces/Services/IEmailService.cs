using SocietyLedger.Application.DTOs.Email;

namespace SocietyLedger.Application.Interfaces.Services
{
    public interface IEmailService
    {
        Task<bool> SendPasswordResetEmailAsync(string email, string resetToken, string resetUrl);
        Task<bool> SendWelcomeEmailAsync(string email, string name, WelcomeEmailData data);
        Task<bool> SendSubscriptionReminderEmailAsync(string email, string name, SubscriptionReminderData data);
        Task<bool> SendContactUsNotificationAsync(ContactUsNotificationData data);
    }
}