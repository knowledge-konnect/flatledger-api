using SocietyLedger.Application.DTOs.Expense;

namespace SocietyLedger.Application.Interfaces.Services
{
    public interface IExpenseService
    {
        Task<ExpenseResponse> CreateExpenseAsync(long userId, CreateExpenseRequest request);
        Task<ExpenseResponse> GetExpenseAsync(Guid publicId, long userId);
        Task<IEnumerable<ExpenseResponse>> GetExpensesBySocietyAsync(long userId);
        Task<IEnumerable<ExpenseResponse>> GetExpensesByDateRangeAsync(long userId, DateOnly startDate, DateOnly endDate);
        Task<IEnumerable<ExpenseResponse>> GetExpensesByCategoryAsync(long userId, string categoryCode);
        Task<IEnumerable<ExpenseCategoryResponse>> GetExpenseCategoriesAsync();
        Task<ExpenseResponse> UpdateExpenseAsync(Guid publicId, long userId, UpdateExpenseRequest request);
        Task DeleteExpenseAsync(Guid publicId, long userId);

        /// <summary>
        /// Returns a paginated, filtered, and sorted list of expenses for the user's society.
        /// All parameters are optional — omitting them returns the first page of all expenses.
        /// </summary>
        Task<PagedExpensesResponse> GetPagedAsync(
            long userId,
            DateOnly? startDate,
            DateOnly? endDate,
            string? categoryCode,
            string? search,
            int page,
            int size,
            string sortBy,
            string sortDir);
    }
}
