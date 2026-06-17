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
        public decimal? SubscribedAmount { get; set; }
        public decimal? PlanMonthlyAmount { get; set; }
        public DateTime? CurrentPeriodEnd { get; set; }
        public string? AmountSource { get; set; }
        public string? Currency { get; set; }
        public int DurationMonths { get; set; } = 1;
        public int? MaxFlats { get; set; }
    }
}