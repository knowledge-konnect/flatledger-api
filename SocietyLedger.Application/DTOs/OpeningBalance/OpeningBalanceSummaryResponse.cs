namespace SocietyLedger.Application.DTOs.OpeningBalance
{
    public class OpeningBalanceSummaryResponse
    {
        public decimal SocietyOpeningAmount { get; set; }
        public decimal TotalMemberDues { get; set; }
        public decimal TotalMemberAdvance { get; set; }
    }
}