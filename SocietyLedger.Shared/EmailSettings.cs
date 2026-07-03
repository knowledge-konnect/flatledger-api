using System.ComponentModel.DataAnnotations;

namespace SocietyLedger.Shared
{
    /// <summary>
    /// Email configuration bound from the "Email" section in appsettings.json.
    /// Used by EmailService, PasswordResetService, SubscriptionReminderService, and ContactService.
    /// </summary>
    public class EmailSettings
    {
        [Required]
        public string ResendApiKey { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string FromAddress { get; set; } = "noreply@flatledger.in";

        public string FromName { get; set; } = "FlatLedger";

        [EmailAddress]
        public string SupportEmail { get; set; } = "support@flatledger.in";

        public string SupportPhone { get; set; } = string.Empty;

        public string LoginUrl { get; set; } = string.Empty;

        public string ContactEmail { get; set; } = string.Empty;

        [Required]
        [Url]
        public string FrontendUrl { get; set; } = string.Empty;

        public int PasswordResetTokenExpiryMinutes { get; set; } = 15;
    }
}
