using SocietyLedger.Application.DTOs.Contact;

namespace SocietyLedger.Application.Interfaces.Services
{
    public interface IContactService
    {
        /// <summary>
        /// Saves the contact form submission to the database, then sends a notification
        /// email to the configured contact address. Email failure is logged but does not
        /// propagate — the submission is always persisted.
        /// </summary>
        Task SubmitAsync(ContactUsRequest request, CancellationToken cancellationToken = default);
    }
}
