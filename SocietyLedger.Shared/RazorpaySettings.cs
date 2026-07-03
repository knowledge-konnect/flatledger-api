using System.ComponentModel.DataAnnotations;

namespace SocietyLedger.Shared
{
    public class RazorpaySettings
    {
        [Required]
        public string KeyId { get; set; } = string.Empty;

        [Required]
        public string KeySecret { get; set; } = string.Empty;

        [Required]
        public string WebhookSecret { get; set; } = string.Empty;
    }
}
