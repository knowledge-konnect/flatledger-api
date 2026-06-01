namespace SocietyLedger.Infrastructure.Services
{
    public class EmailSettings
    {
        public string ResendApiKey { get; set; } = string.Empty;
        public string FromAddress { get; set; } = "noreply@flatledger.in";
        public string FromName { get; set; } = "FlatLedger";
        public string SupportEmail { get; set; } = "support@flatledger.in";
        public string SupportPhone { get; set; } = string.Empty;
        public string LoginUrl { get; set; } = string.Empty;
        /// <summary>
        /// Destination address for Contact Us form notifications.
        /// Override via environment variable Email__ContactEmail.
        /// Falls back to SupportEmail when not set.
        /// </summary>
        public string ContactEmail { get; set; } = string.Empty;
    }
}
