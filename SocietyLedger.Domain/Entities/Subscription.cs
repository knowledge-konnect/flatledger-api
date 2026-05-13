namespace SocietyLedger.Domain.Entities
{
    /// <summary>
    /// Represents a society's SaaS subscription. A society may be on a free trial or a paid plan.
    /// Billing is always society-scoped — never user-scoped.
    /// Status transitions are recorded in <see cref="SubscriptionEvent"/>.
    /// </summary>
    public class Subscription
    {
        public Guid Id { get; set; }
        public long UserId { get; set; }   // The admin user who created/manages this subscription
        public long SocietyId { get; set; } // The society this subscription belongs to
        public Guid PlanId { get; set; }
        public string Status { get; set; } = null!;
        public decimal SubscribedAmount { get; set; }
        public string? Currency { get; set; }
        public DateTime? CurrentPeriodStart { get; set; }
        public DateTime? CurrentPeriodEnd { get; set; }
        public DateTime? TrialStart { get; set; }
        public DateTime? TrialEnd { get; set; }
        public DateTime? CancelledAt { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        public Plan? Plan { get; set; }
        public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
    }

    /// <summary>Available SaaS subscription plans (e.g., Monthly, Annual).</summary>
    public class Plan
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public decimal Price { get; set; }
        public string Currency { get; set; } = null!;
        public bool? IsActive { get; set; }
        public DateTime? CreatedAt { get; set; }

        // New fields for flat-based pricing
        public int MaxFlats { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsPopular { get; set; }
        public string? Description { get; set; }
        public int? DiscountPercentage { get; set; }
        public string PlanGroup { get; set; } = null!;
        public int DurationMonths { get; set; }
    }

    /// <summary>
    /// Billing invoice generated for a subscription period.
    /// Tracks payment status and links back to the parent subscription.
    /// </summary>
    public class Invoice
    {
        public Guid Id { get; set; }
        public long UserId { get; set; }
        public Guid? SubscriptionId { get; set; }
        public string InvoiceNumber { get; set; } = null!;
        public string InvoiceType { get; set; } = null!;
        public decimal Amount { get; set; }
        public decimal? TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public string? Currency { get; set; }
        public string Status { get; set; } = null!;
        public DateOnly? PeriodStart { get; set; }
        public DateOnly? PeriodEnd { get; set; }
        public DateOnly DueDate { get; set; }
        public DateTime? PaidDate { get; set; }
        public string? PaymentMethod { get; set; }
        public string? PaymentReference { get; set; }
        public string? Description { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        public Subscription? Subscription { get; set; }
    }

    /// <summary>
    /// Audit log of subscription lifecycle events (trial started, upgraded, cancelled, expired, etc.).
    /// </summary>
    public class SubscriptionEvent
    {
        public Guid Id { get; set; }
        public long UserId { get; set; }
        public long SocietyId { get; set; }
        public Guid? SubscriptionId { get; set; }
        public string EventType { get; set; } = null!;
        public string? OldStatus { get; set; }
        public string? NewStatus { get; set; }
        public decimal? Amount { get; set; }
        public string? Metadata { get; set; }
        public DateTime? CreatedAt { get; set; }

        // Navigation properties
        public Subscription? Subscription { get; set; }
    }
}