using SocietyLedger.Application.DTOs.ContactUs;

namespace SocietyLedger.Application.Interfaces.Services
{
    public interface IContactUsService
    {
        Task<bool> SubmitContactUsAsync(ContactUsRequest request, string ipAddress);
    }
}