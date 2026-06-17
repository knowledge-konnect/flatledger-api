using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SocietyLedger.Application.DTOs.ContactUs;
using SocietyLedger.Application.DTOs.Email;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Shared;

namespace SocietyLedger.Infrastructure.Services
{
    public class ContactUsService : IContactUsService
    {
        private readonly IEmailService _emailService;
        private readonly EmailSettings _emailSettings;
        private readonly ILogger<ContactUsService> _logger;
        private readonly AppDbContext _dbContext;

        public ContactUsService(
            IEmailService emailService,
            IOptions<EmailSettings> emailSettings,
            ILogger<ContactUsService> logger,
            AppDbContext dbContext)
        {
            _emailService = emailService;
            _emailSettings = emailSettings.Value;
            _logger = logger;
            _dbContext = dbContext;
        }

        public async Task<bool> SubmitContactUsAsync(ContactUsRequest request, string ipAddress)
        {
            try
            {
                // Prepare notification data
                var notificationData = new ContactUsNotificationData
                {
                    SubmitterName = request.Name,
                    SubmitterEmail = request.Email,
                    Phone = request.Phone,
                    Subject = request.Subject,
                    Message = request.Message,
                    SubmittedAt = DateTime.UtcNow
                };

                // Send notification email
                var emailSent = await _emailService.SendContactUsNotificationAsync(notificationData);

                if (emailSent)
                {
                    _logger.LogInformation("Contact Us form submitted successfully by {Name} ({Email}) from IP: {IP}", 
                        request.Name, request.Email, ipAddress);
                }
                else
                {
                    _logger.LogError("Failed to send Contact Us notification for submission from {Email}", request.Email);
                }

                await _dbContext.email_notification_logs.AddAsync(new Persistence.Entities.email_notification_log
                {
                    notification_type = "contact_us",
                    recipient_email = request.Email,
                    recipient_name = request.Name,
                    subject = $"Contact Us Form Submission: {request.Subject}",
                    sent_at = DateTime.UtcNow,
                    sent_by_system = false,
                    status = emailSent ? "sent" : "failed",
                    error_message = emailSent ? null : "Email delivery failed"
                });
                await _dbContext.SaveChangesAsync();

                return emailSent;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Contact Us submission from {Email}", request.Email);
                return false;
            }
        }
    }
}