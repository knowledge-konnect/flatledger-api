namespace SocietyLedger.Application.DTOs.Flat
{
    public class FlatLedgerResponse
    {
        public Guid FlatPublicId { get; set; }
        public string FlatNo { get; set; } = null!;
        public string? OwnerName { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal ClosingBalance { get; set; }
        public List<FlatLedgerEntryDto> Entries { get; set; } = new List<FlatLedgerEntryDto>();

        /// <summary>
        /// All non-cancelled bills for this flat with their current status.
        /// Used by the frontend to detect which bills were cleared by a payment.
        /// </summary>
        public List<FlatLedgerBillDto> Bills { get; set; } = new List<FlatLedgerBillDto>();

        /// <summary>Total outstanding (OB + bill dues - unallocated advance).</summary>
        public decimal TotalOutstanding { get; set; }

        /// <summary>Sum of advance payments not yet linked to any bill.</summary>
        public decimal TotalAdvance { get; set; }
    }

    public class FlatLedgerBillDto
    {
        public Guid BillPublicId { get; set; }
        public string Period { get; set; } = null!;
        public decimal Amount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal BalanceAmount { get; set; }
        public string StatusCode { get; set; } = null!;
    }
}
