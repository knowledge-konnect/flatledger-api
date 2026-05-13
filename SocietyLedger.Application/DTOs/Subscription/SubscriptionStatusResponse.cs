namespace SocietyLedger.Application.DTOs.Subscription
{
    public class SubscriptionStatusResponse
    {
        public string Status { get; set; } = null!;
        public int? TrialDaysRemaining { get; set; }
        public DateTime? TrialEndDate { get; set; }
        public bool AccessAllowed { get; set; }
        public string? PlanName { get; set; }
        public decimal? MonthlyAmount { get; set; }
        /// <summary>The locked price the society subscribed at. Alias for MonthlyAmount.</summary>
        public decimal? SubscribedAmount => MonthlyAmount;
        public string? Currency { get; set; }
        public DateTime? CurrentPeriodEnd { get; set; }
    }
}