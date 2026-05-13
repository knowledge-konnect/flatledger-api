using Microsoft.Extensions.Logging;
using SocietyLedger.Application.DTOs.Expense;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Services.Common;

namespace SocietyLedger.Infrastructure.Services
{
    public class ExpenseService : IExpenseService
    {
        private readonly IExpenseRepository _expenseRepo;
        private readonly ISocietyRepository _societyRepo;
        private readonly IUserContext _userContext;
        private readonly IDashboardService _dashboardService;
        private readonly ILogger<ExpenseService> _logger;

        public ExpenseService(
            IExpenseRepository expenseRepo,
            ISocietyRepository societyRepo,
            IUserContext userContext,
            IDashboardService dashboardService,
            ILogger<ExpenseService> logger)
        {
            _expenseRepo      = expenseRepo ?? throw new ArgumentNullException(nameof(expenseRepo));
            _societyRepo      = societyRepo ?? throw new ArgumentNullException(nameof(societyRepo));
            _userContext      = userContext ?? throw new ArgumentNullException(nameof(userContext));
            _dashboardService = dashboardService ?? throw new ArgumentNullException(nameof(dashboardService));
            _logger           = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<ExpenseResponse> CreateExpenseAsync(long userId, CreateExpenseRequest request)
        {
            var societyId = await _userContext.GetSocietyIdAsync(userId);

            // Validate business date against the society's financial epoch.
            var onboardingDate = await GetOnboardingDateAsync(societyId);
            if (request.Date < onboardingDate)
                throw new ValidationException(
                    $"Expense date ({request.Date:yyyy-MM-dd}) cannot be earlier than " +
                    $"the society onboarding date ({onboardingDate:yyyy-MM-dd}).");

            // #7 — Near-duplicate detection: same date, amount, category, and vendor within 5 minutes.
            var fiveMinutesAgo = DateTime.UtcNow.AddMinutes(-5);
            var isDuplicate = await _expenseRepo.IsDuplicateRecentAsync(
                societyId, request.Date, request.Amount, request.CategoryCode,
                request.Vendor ?? string.Empty, fiveMinutesAgo);

            if (isDuplicate)
                throw new ConflictException(
                    "A similar expense (same date, amount, category, and vendor) was recorded in the last 5 minutes. " +
                    "If this is intentional, please wait a moment and try again.");

            // Create expense with public_id and created_by
            var expenseEntity = new SocietyLedger.Infrastructure.Persistence.Entities.expense
            {
                public_id = Guid.NewGuid(),
                society_id = societyId,
                date_incurred = request.Date,
                category_code = request.CategoryCode,
                vendor = request.Vendor,
                description = request.Description,
                amount = request.Amount,
                created_by = userId,
                created_at = DateTime.UtcNow
            };

            await _expenseRepo.AddAsync(expenseEntity);
            await _expenseRepo.SaveChangesAsync();

            _logger.LogInformation("Expense created successfully by user {UserId} for society {SocietyId}", userId, societyId);
            _dashboardService.InvalidateDashboardCache(societyId);
            
            // Reload to get navigation properties
            var createdExpense = await _expenseRepo.GetByPublicIdAsync(expenseEntity.public_id, societyId);
            if (createdExpense == null)
                throw new InvalidOperationException("Failed to retrieve created expense");
            
            return MapToResponse(createdExpense);
        }

        public async Task<ExpenseResponse> GetExpenseAsync(Guid publicId, long userId)
        {
            var societyId = await _userContext.GetSocietyIdAsync(userId);
            
            var expenseEntity = await _expenseRepo.GetByPublicIdAsync(publicId, societyId);
            if (expenseEntity == null)
                throw new NotFoundException("Expense", publicId.ToString());

            return MapToResponse(expenseEntity);
        }

        public async Task<IEnumerable<ExpenseResponse>> GetExpensesBySocietyAsync(long userId)
        {
            var societyId = await _userContext.GetSocietyIdAsync(userId);
            
            var expenses = await _expenseRepo.GetBySocietyIdAsync(societyId);
            return expenses.Select(MapToResponse);
        }

        public async Task<IEnumerable<ExpenseResponse>> GetExpensesByDateRangeAsync(long userId, DateOnly startDate, DateOnly endDate)
        {
            var societyId = await _userContext.GetSocietyIdAsync(userId);
            
            var expenses = await _expenseRepo.GetByDateRangeAsync(societyId, startDate, endDate);
            return expenses.Select(MapToResponse);
        }

        public async Task<IEnumerable<ExpenseResponse>> GetExpensesByCategoryAsync(long userId, string categoryCode)
        {
            var societyId = await _userContext.GetSocietyIdAsync(userId);
            
            var expenses = await _expenseRepo.GetByCategoryAsync(societyId, categoryCode);
            return expenses.Select(MapToResponse);
        }

        public async Task<IEnumerable<ExpenseCategoryResponse>> GetExpenseCategoriesAsync()
        {
            return await _expenseRepo.GetCategoriesAsync();
        }

        public async Task<ExpenseResponse> UpdateExpenseAsync(Guid publicId, long userId, UpdateExpenseRequest request)
        {
            var societyId = await _userContext.GetSocietyIdAsync(userId);

            // Check existence before any validation
            var existing = await _expenseRepo.GetByPublicIdAsync(publicId, societyId);
            if (existing == null)
                throw new NotFoundException("Expense", publicId.ToString());

            // Validate updated date against the society's financial epoch.
            if (request.Date.HasValue)
            {
                var onboardingDate = await GetOnboardingDateAsync(societyId);
                if (request.Date.Value < onboardingDate)
                    throw new ValidationException(
                        $"Expense date ({request.Date.Value:yyyy-MM-dd}) cannot be earlier than " +
                        $"the society onboarding date ({onboardingDate:yyyy-MM-dd}).");
            }

            var updatedExpense = await _expenseRepo.UpdateFieldsAsync(publicId, societyId, request);
            if (updatedExpense == null)
                throw new InvalidOperationException("Failed to retrieve updated expense");

            _dashboardService.InvalidateDashboardCache(societyId);

            return MapToResponse(updatedExpense);
        }

        public async Task DeleteExpenseAsync(Guid publicId, long userId)
        {
            var societyId = await _userContext.GetSocietyIdAsync(userId);

            var expenseEntity = await _expenseRepo.GetByPublicIdAsync(publicId, societyId);
            if (expenseEntity == null)
                throw new NotFoundException("Expense", publicId.ToString());

            await _expenseRepo.DeleteByPublicIdAsync(publicId, societyId);
            await _expenseRepo.SaveChangesAsync();

            _dashboardService.InvalidateDashboardCache(societyId);
            _logger.LogInformation("Expense deleted successfully: {PublicId}", publicId);
        }

        public async Task<PagedExpensesResponse> GetPagedAsync(
            long userId,
            DateOnly? startDate,
            DateOnly? endDate,
            string? categoryCode,
            string? search,
            int page,
            int size,
            string sortBy,
            string sortDir)
        {
            var societyId = await _userContext.GetSocietyIdAsync(userId);

            var (items, totalCount) = await _expenseRepo.GetPagedAsync(
                societyId, startDate, endDate, categoryCode, search, page, size, sortBy, sortDir);

            var totalPages = size > 0 ? (int)Math.Ceiling((double)totalCount / size) : 0;

            return new PagedExpensesResponse
            {
                Content       = items.Select(MapToResponse).ToList(),
                TotalElements = totalCount,
                TotalPages    = totalPages,
                Page          = page,
                Size          = size
            };
        }

        private ExpenseResponse MapToResponse(ExpenseEntity entity)
        {
            return new ExpenseResponse
            {
                PublicId = entity.PublicId,
                SocietyPublicId = entity.SocietyPublicId,
                DateIncurred = entity.DateIncurred,
                CategoryCode = entity.CategoryCode,
                Vendor = entity.Vendor,
                Description = entity.Description,
                Amount = entity.Amount,
                ApprovedByName = entity.ApprovedByName,
                CreatedByName = entity.CreatedByName,
                CreatedAt = entity.CreatedAt
            };
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the society's onboarding date. Throws <see cref="NotFoundException"/>
        /// if the society is not found (soft-delete check included).
        /// </summary>
        private async Task<DateOnly> GetOnboardingDateAsync(long societyId)
        {
            var date = await _societyRepo.GetOnboardingDateAsync(societyId);

            if (date is null)
                throw new NotFoundException("Society", societyId.ToString());

            return date.Value;
        }    }
}