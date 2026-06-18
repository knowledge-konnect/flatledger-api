namespace SocietyLedger.Application.DTOs.Expense
{
    public class PagedExpensesResponse
    {
        public List<ExpenseResponse> Content { get; set; } = new();
        public long TotalElements { get; set; }
        public int TotalPages { get; set; }
        public int Page { get; set; }
        public int Size { get; set; }
    }
}
