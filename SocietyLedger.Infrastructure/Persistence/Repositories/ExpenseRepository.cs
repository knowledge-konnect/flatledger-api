using Microsoft.EntityFrameworkCore;
using SocietyLedger.Application.DTOs.Expense;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Persistence.Entities;
using SocietyLedger.Infrastructure.Persistence.Extensions;

namespace SocietyLedger.Infrastructure.Persistence.Repositories
{
    public class ExpenseRepository : IExpenseRepository
    {
        private readonly AppDbContext _db;

        public ExpenseRepository(AppDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<ExpenseEntity?> GetByPublicIdAsync(Guid publicId, long societyId)
        {
            var expense = await _db.expenses
                .ForSociety(societyId)
                .Include(e => e.created_byNavigation)
                .Include(e => e.approved_byNavigation)
                .Include(e => e.society)
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.public_id == publicId);
            
            return expense != null ? MapToDto(expense) : null;
        }

        public async Task<IEnumerable<ExpenseEntity>> GetBySocietyIdAsync(long societyId)
        {
            var expenses = await _db.expenses
                .ForSociety(societyId)
                .Include(e => e.created_byNavigation)
                .Include(e => e.approved_byNavigation)
                .Include(e => e.society)
                .AsNoTracking()
                .OrderByDescending(e => e.date_incurred)
                .ToListAsync();
            
            return expenses.Select(MapToDto);
        }

        public async Task<IEnumerable<ExpenseEntity>> GetByDateRangeAsync(long societyId, DateOnly startDate, DateOnly endDate)
        {
            var expenses = await _db.expenses
                .ForSociety(societyId)
                .Where(e => e.date_incurred >= startDate && e.date_incurred <= endDate)
                .Include(e => e.created_byNavigation)
                .Include(e => e.approved_byNavigation)
                .Include(e => e.society)
                .AsNoTracking()
                .OrderByDescending(e => e.date_incurred)
                .ToListAsync();
            
            return expenses.Select(MapToDto);
        }

        public async Task<IEnumerable<ExpenseEntity>> GetByCategoryAsync(long societyId, string categoryCode)
        {
            var expenses = await _db.expenses
                .ForSociety(societyId)
                .Where(e => e.category_code == categoryCode)
                .Include(e => e.created_byNavigation)
                .Include(e => e.approved_byNavigation)
                .Include(e => e.society)
                .AsNoTracking()
                .OrderByDescending(e => e.date_incurred)
                .ToListAsync();
            
            return expenses.Select(MapToDto);
        }

        public async Task AddAsync(object entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            await _db.expenses.AddAsync((expense)entity);
        }

        public async Task UpdateAsync(object entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            _db.expenses.Update((expense)entity);
        }

        public async Task DeleteByPublicIdAsync(Guid publicId, long societyId)
        {
            var entity = await _db.expenses
                .ForSociety(societyId)
                .FirstOrDefaultAsync(e => e.public_id == publicId);
            
            if (entity != null)
            {
                entity.is_deleted = true;
                entity.deleted_at = DateTime.UtcNow;
            }
        }

        public async Task SaveChangesAsync()
        {
            await _db.SaveChangesAsync();
        }

        public Task<bool> IsDuplicateRecentAsync(long societyId, DateOnly date, decimal amount, string categoryCode, string vendor, DateTime since) =>
            _db.expenses.AnyAsync(e =>
                e.society_id    == societyId
             && !e.is_deleted
             && e.date_incurred == date
             && e.amount        == amount
             && e.category_code == categoryCode
             && (e.vendor ?? string.Empty) == (vendor ?? string.Empty)
             && e.created_at   >= since);

        public async Task<ExpenseEntity?> UpdateFieldsAsync(Guid publicId, long societyId, UpdateExpenseRequest request)
        {
            var entity = await _db.expenses
                .ForSociety(societyId)
                .FirstOrDefaultAsync(e => e.public_id == publicId);

            if (entity == null)
                return null;

            if (request.Date.HasValue)        entity.date_incurred  = request.Date.Value;
            if (request.CategoryCode != null) entity.category_code  = request.CategoryCode;
            if (request.Vendor       != null) entity.vendor         = request.Vendor;
            if (request.Description  != null) entity.description    = request.Description;
            if (request.Amount.HasValue)      entity.amount         = request.Amount.Value;

            await _db.SaveChangesAsync();

            // Reload with navigation properties
            return await GetByPublicIdAsync(publicId, societyId);
        }

        public async Task<IReadOnlyList<ExpenseCategoryResponse>> GetCategoriesAsync() =>
            await _db.expense_categories
                .AsNoTracking()
                .OrderBy(c => c.display_name)
                .Select(c => new ExpenseCategoryResponse { Code = c.code, DisplayName = c.display_name })
                .ToListAsync();

        private static readonly HashSet<string> AllowedSortFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "dateIncurred", "amount", "categoryCode"
        };

        public async Task<(IReadOnlyList<ExpenseEntity> Items, long TotalCount)> GetPagedAsync(
            long societyId, DateOnly? startDate, DateOnly? endDate,
            string? categoryCode, string? search,
            int page, int size, string sortBy, string sortDir)
        {
            var query = _db.expenses
                .Where(e => e.society_id == societyId && !e.is_deleted)
                .AsQueryable();

            if (startDate.HasValue)
                query = query.Where(e => e.date_incurred >= startDate.Value);
            if (endDate.HasValue)
                query = query.Where(e => e.date_incurred <= endDate.Value);
            if (!string.IsNullOrWhiteSpace(categoryCode))
                query = query.Where(e => e.category_code == categoryCode);
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(e =>
                    (e.vendor      != null && EF.Functions.ILike(e.vendor,      $"%{search}%")) ||
                    (e.description != null && EF.Functions.ILike(e.description, $"%{search}%")));

            query = (sortBy.ToLower(), sortDir.ToLower()) switch
            {
                ("dateincurred", "asc")  => query.OrderBy(e => e.date_incurred),
                ("dateincurred", _)      => query.OrderByDescending(e => e.date_incurred),
                ("amount", "asc")        => query.OrderBy(e => e.amount),
                ("amount", _)            => query.OrderByDescending(e => e.amount),
                ("categorycode", "asc")  => query.OrderBy(e => e.category_code),
                ("categorycode", _)      => query.OrderByDescending(e => e.category_code),
                _                        => query.OrderByDescending(e => e.date_incurred),
            };

            var totalCount = await query.LongCountAsync();

            var items = await query
                .Skip(page * size)
                .Take(size)
                .Select(e => new ExpenseEntity
                {
                    PublicId       = e.public_id,
                    SocietyId      = e.society_id,
                    DateIncurred   = e.date_incurred,
                    CategoryCode   = e.category_code,
                    Vendor         = e.vendor,
                    Description    = e.description,
                    Amount         = e.amount,
                    ApprovedBy     = e.approved_by,
                    ApprovedByName = e.approved_byNavigation != null ? e.approved_byNavigation.name : null,
                    CreatedBy      = e.created_by,
                    CreatedByName  = e.created_byNavigation  != null ? e.created_byNavigation.name  : null,
                    CreatedAt      = e.created_at
                })
                .AsNoTracking()
                .ToListAsync();

            return (items, totalCount);
        }

        private ExpenseEntity MapToDto(expense entity)
        {
            return new ExpenseEntity
            {
                PublicId = entity.public_id,
                SocietyId = entity.society_id,
                SocietyPublicId = entity.society?.public_id ?? Guid.Empty,
                DateIncurred = entity.date_incurred,
                CategoryCode = entity.category_code,
                Vendor = entity.vendor,
                Description = entity.description,
                Amount = entity.amount,
                AttachmentId = entity.attachment_id,
                ApprovedBy = entity.approved_by,
                ApprovedByName = entity.approved_byNavigation?.name,
                CreatedBy = entity.created_by,
                CreatedByName = entity.created_byNavigation?.name,
                CreatedAt = entity.created_at
            };
        }
    }
}
