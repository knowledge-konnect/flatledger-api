namespace SocietyLedger.Application.Interfaces.Services
{
    public interface IEmailService
    {
        /// <summary>Sends a password reset email with the given token link.</summary>
        Task SendPasswordResetEmailAsync(string userEmail, string userName, string resetLink, CancellationToken cancellationToken = default);
    }
}
