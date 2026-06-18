namespace SocietyLedger.Application.Interfaces.Repositories
{
    using SocietyLedger.Application.DTOs.Expense;

    public interface IExpenseRepository
    {
        Task<ExpenseEntity?> GetByPublicIdAsync(Guid publicId, long societyId);
        Task<IEnumerable<ExpenseEntity>> GetBySocietyIdAsync(long societyId);
        Task<IEnumerable<ExpenseEntity>> GetByDateRangeAsync(long societyId, DateOnly startDate, DateOnly endDate);
        Task<IEnumerable<ExpenseEntity>> GetByCategoryAsync(long societyId, string categoryCode);
        Task AddAsync(object entity);
        Task UpdateAsync(object entity);
        Task DeleteByPublicIdAsync(Guid publicId, long societyId);
        Task SaveChangesAsync();

        /// <summary>
        /// Checks for a near-duplicate expense (same date/amount/category/vendor within 5 minutes).
        /// </summary>
        Task<bool> IsDuplicateRecentAsync(long societyId, DateOnly date, decimal amount, string categoryCode, string vendor, DateTime since);

        /// <summary>
        /// Applies partial-update fields to the expense entity in-repo, avoiding a double-fetch in the service.
        /// Returns the updated entity (with navigation properties) or null when not found.
        /// </summary>
        Task<ExpenseEntity?> UpdateFieldsAsync(Guid publicId, long societyId, UpdateExpenseRequest request);

        /// <summary>Returns all active expense categories ordered by display name.</summary>
        Task<IReadOnlyList<ExpenseCategoryResponse>> GetCategoriesAsync();

        /// <summary>Returns a paged result set with total count for the given filters.</summary>
        Task<(IReadOnlyList<ExpenseEntity> Items, long TotalCount)> GetPagedAsync(
            long societyId,
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
