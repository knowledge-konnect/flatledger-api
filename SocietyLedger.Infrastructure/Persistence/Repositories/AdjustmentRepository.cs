using Microsoft.EntityFrameworkCore;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Infrastructure.Persistence.Contexts;

namespace SocietyLedger.Infrastructure.Persistence.Repositories
{
    public class AdjustmentRepository : IAdjustmentRepository
    {
        private readonly AppDbContext _db;

        public AdjustmentRepository(AppDbContext db) =>
            _db = db ?? throw new ArgumentNullException(nameof(db));

        public async Task<IReadOnlyList<AdjustmentLedgerEntry>> GetByFlatIdAsync(long flatId, DateTime? startDate, DateTime? endDate)
        {
            var query = _db.adjustments
                .Where(a => a.flat_id == flatId && !a.is_deleted)
                .AsQueryable();

            if (startDate.HasValue) query = query.Where(a => a.created_at >= startDate.Value);
            if (endDate.HasValue)   query = query.Where(a => a.created_at < endDate.Value.Date.AddDays(1));

            return await query
                .OrderBy(a => a.created_at)
                .Select(a => new AdjustmentLedgerEntry(a.created_at, a.entry_type, a.reason, a.amount))
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<decimal> GetTotalAmountBeforeDateAsync(long flatId, DateTime before) =>
            await _db.adjustments
                .Where(a => a.flat_id == flatId && a.created_at < before && !a.is_deleted)
                .SumAsync(a => (decimal?)a.amount) ?? 0m;

        public async Task<decimal> GetOpeningBalanceRemainingAsync(long flatId) =>
            await _db.adjustments
                .Where(a => a.flat_id == flatId
                         && a.entry_type == EntryTypeCodes.OpeningBalance
                         && !a.is_deleted)
                .SumAsync(a => (decimal?)a.remaining_amount) ?? 0m;
    }
}
