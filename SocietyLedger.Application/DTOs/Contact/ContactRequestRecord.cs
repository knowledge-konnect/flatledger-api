namespace SocietyLedger.Application.DTOs.Contact
{
    /// <summary>Represents a saved contact form submission returned after persistence.</summary>
    public class ContactRequestRecord
    {
        public Guid PublicId { get; set; }
        public string Name { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string? Subject { get; set; }
        public string Message { get; set; } = null!;
        public string Status { get; set; } = "New";
        public DateTime CreatedAt { get; set; }
    }
}
