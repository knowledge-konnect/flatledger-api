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
    }
}
