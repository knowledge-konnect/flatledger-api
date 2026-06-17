namespace SocietyLedger.Application.DTOs.Email
{
    public abstract class EmailBaseData
    {
        public string RecipientName { get; set; } = string.Empty;
        public string RecipientEmail { get; set; } = string.Empty;
    }

    public class WelcomeEmailData : EmailBaseData
    {
        public string SocietyName { get; set; } = string.Empty;
        public string AdminName { get; set; } = string.Empty;
        public string LoginUrl { get; set; } = string.Empty;
        public string CurrentPlanName { get; set; } = string.Empty;
        public DateTime? TrialEndDate { get; set; }
        public DateTime? SubscriptionExpiryDate { get; set; }
    }

    public class SubscriptionReminderData : EmailBaseData
    {
        public string SocietyName { get; set; } = string.Empty;
        public string CurrentPlan { get; set; } = string.Empty;
        public DateTime ExpiryDate { get; set; }
        public string RenewSubscriptionLink { get; set; } = string.Empty;
        public int DaysUntilExpiry { get; set; }
    }

    public class ContactUsNotificationData : EmailBaseData
    {
        public string Subject { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public DateTime SubmittedAt { get; set; }

        // Semantic aliases — the base class RecipientName/Email here represent
        // the form submitter, not the notification email recipient (which is SupportEmail).
        public string SubmitterName
        {
            get => RecipientName;
            set => RecipientName = value;
        }

        public string SubmitterEmail
        {
            get => RecipientEmail;
            set => RecipientEmail = value;
        }
    }
}