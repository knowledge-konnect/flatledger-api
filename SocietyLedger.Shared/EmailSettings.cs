namespace SocietyLedger.Shared
{
    public class EmailSettings
    {
        public string ResendApiKey { get; set; } = string.Empty;
        public string SenderEmail { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        public string FrontendUrl { get; set; } = string.Empty;
        public string SupportEmail { get; set; } = string.Empty;
        public int PasswordResetTokenExpiryMinutes { get; set; } = 30;
    }
}