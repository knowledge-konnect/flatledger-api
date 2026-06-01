using SocietyLedger.Application.DTOs.Contact;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Persistence.Entities;

namespace SocietyLedger.Infrastructure.Persistence.Repositories
{
    public class ContactRequestRepository : IContactRequestRepository
    {
        private readonly AppDbContext _db;

        public ContactRequestRepository(AppDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<ContactRequestRecord> AddAsync(ContactRequestRecord record, CancellationToken cancellationToken = default)
        {
            var entity = new contact_request
            {
                name    = record.Name,
                email   = record.Email,
                subject = record.Subject,
                message = record.Message,
                status  = record.Status
            };

            _db.contact_requests.Add(entity);
            await _db.SaveChangesAsync(cancellationToken);

            // Return DB-generated fields back to the caller.
            record.PublicId   = entity.public_id;
            record.CreatedAt  = entity.created_at;
            return record;
        }
    }
}
