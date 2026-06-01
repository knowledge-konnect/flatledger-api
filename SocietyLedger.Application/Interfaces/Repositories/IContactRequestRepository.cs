using SocietyLedger.Application.DTOs.Contact;

namespace SocietyLedger.Application.Interfaces.Repositories
{
    public interface IContactRequestRepository
    {
        /// <summary>Persists a new contact request and returns it with database-generated fields populated.</summary>
        Task<ContactRequestRecord> AddAsync(ContactRequestRecord record, CancellationToken cancellationToken = default);
    }
}
