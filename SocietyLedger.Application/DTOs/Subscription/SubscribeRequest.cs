namespace SocietyLedger.Application.DTOs.Subscription
{
    public class SubscribeRequest
    {
        public Guid PlanId { get; set; }
        public string PaymentMethod { get; set; } = null!; // "razorpay", "bank_transfer", etc.
        public string? PaymentReference { get; set; } // For offline payments
        // Note: Amount is intentionally absent. The subscribed amount is always derived
        // from plan.price at subscription time — client-supplied prices are not accepted.
    }
}