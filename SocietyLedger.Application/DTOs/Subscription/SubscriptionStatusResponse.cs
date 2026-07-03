namespace SocietyLedger.Application.DTOs.Subscription
{
    public class SubscriptionStatusResponse
    {
        public string Status { get; set; } = null!;
        public int? TrialDaysRemaining { get; set; }
        public DateTime? TrialEndDate { get; set; }
        public bool AccessAllowed { get; set; }
        public string? PlanName { get; set; }
        /// <summary>The plan's listed monthly amount.</summary>
        public decimal? MonthlyAmount { get; set; }
        /// <summary>The amount the society actually subscribed at (locked in at subscribe time).</summary>
        public decimal? SubscribedAmount { get; set; }
        public string? Currency { get; set; }
        /// <summary>When the current billing period ends. Null for trial subscriptions.</summary>
        public DateTime? CurrentPeriodEnd { get; set; }
        /// <summary>Number of months per billing cycle (1 = monthly, 12 = annual).</summary>
        public int? DurationMonths { get; set; }
        /// <summary>Maximum number of flats allowed under this plan.</summary>
        public int? MaxFlats { get; set; }
    }
}
