using Microsoft.Extensions.Logging;
using SocietyLedger.Application.DTOs.Contact;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;

namespace SocietyLedger.Infrastructure.Services
{
    public class ContactService : IContactService
    {
        private readonly IContactRequestRepository _repo;
        private readonly IEmailService _emailService;
        private readonly ILogger<ContactService> _logger;

        public ContactService(
            IContactRequestRepository repo,
            IEmailService emailService,
            ILogger<ContactService> logger)
        {
            _repo = repo;
            _emailService = emailService;
            _logger = logger;
        }

        public async Task SubmitAsync(ContactUsRequest request, CancellationToken cancellationToken = default)
        {
            // Persist first — the record must survive even if the email fails.
            var record = new ContactRequestRecord
            {
                Name    = request.Name.Trim(),
                Email   = request.Email.Trim().ToLowerInvariant(),
                Subject = string.IsNullOrWhiteSpace(request.Subject) ? null : request.Subject.Trim(),
                Message = request.Message.Trim(),
                Status  = "New"
            };

            await _repo.AddAsync(record, cancellationToken);

            _logger.LogInformation(
                "Contact request saved (id: {PublicId}) from {Email}",
                record.PublicId, record.Email);

            // Send notification email. Failure is isolated — submission is already committed.
            try
            {
                await _emailService.SendContactUsNotificationAsync(
                    record.Name,
                    record.Email,
                    record.Subject ?? string.Empty,
                    record.Message,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Contact notification email failed for request {PublicId} — submission already saved",
                    record.PublicId);
            }
        }
    }
}
