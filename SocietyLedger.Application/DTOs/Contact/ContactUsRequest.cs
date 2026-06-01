namespace SocietyLedger.Application.DTOs.Contact
{
    public class ContactUsRequest
    {
        public string Name { get; set; } = null!;
        public string Email { get; set; } = null!;
        /// <summary>Optional subject line for the enquiry.</summary>
        public string? Subject { get; set; }
        public string Message { get; set; } = null!;
    }
}
