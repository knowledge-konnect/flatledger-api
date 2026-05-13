namespace SocietyLedger.Application.DTOs.Subscription
{
    public class SubscribeRequest
    {
        public Guid PlanId { get; set; }

        /// <summary>
        /// Payment method used. Defaults to "razorpay" when omitted.
        /// Allowed values: razorpay, bank_transfer, upi, cash, cheque.
        /// </summary>
        public string? PaymentMethod { get; set; }

        /// <summary>Reference number for offline payments (bank_transfer, cheque, etc.).</summary>
        public string? PaymentReference { get; set; }

        // Note: Amount is intentionally absent. The subscribed amount is always derived
        // from plan.price at subscription time — client-supplied prices are not accepted.
    }
}