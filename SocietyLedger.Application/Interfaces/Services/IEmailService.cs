namespace SocietyLedger.Application.Interfaces.Services
{
    public interface IEmailService
    {
        /// <summary>Sends a password reset email with the given token link.</summary>
        /// <param name="logResetLinkInDevelopment">When true, logs the link locally (never in production).</param>
        Task SendPasswordResetEmailAsync(
            string userEmail,
            string userName,
            string resetLink,
            bool logResetLinkInDevelopment = false,
            CancellationToken cancellationToken = default);

        /// <summary>Sends a welcome email after a new society is registered.</summary>
        Task SendWelcomeEmailAsync(
            string userEmail,
            string adminName,
            string societyName,
            string planName,
            DateTime trialOrExpiryEnd,
            string loginUrl,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a subscription expiry reminder email.
        /// </summary>
        /// <param name="stage">"7d" | "1d" | "0d" — number of days before expiry.</param>
        Task SendSubscriptionExpiryReminderAsync(
            string userEmail,
            string societyName,
            string planName,
            DateTime expiryDate,
            string renewUrl,
            string stage,
            CancellationToken cancellationToken = default);

        /// <summary>Sends a contact-us form submission to the configured support address.</summary>
        Task SendContactUsNotificationAsync(
            string senderName,
            string senderEmail,
            string subject,
            string message,
            CancellationToken cancellationToken = default);
    }
}
