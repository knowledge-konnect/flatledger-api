using Microsoft.EntityFrameworkCore;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Persistence.Entities;

namespace SocietyLedger.Infrastructure.Persistence.Repositories
{
    public class BillRepository : IBillRepository
    {
        private readonly AppDbContext _db;
        public BillRepository(AppDbContext db) => _db = db;

        public Task<bool> ExistsForPeriodAsync(long societyId, string period) =>
            _db.bills.AnyAsync(b => b.society_id == societyId && b.period == period && !b.is_deleted);

        public Task<int> CountForPeriodAsync(long societyId, string period) =>
            _db.bills.CountAsync(b => b.society_id == societyId && b.period == period && !b.is_deleted);

        public Task<bool> ExistsForFlatAndPeriodAsync(long flatId, string period) =>
            _db.bills.AnyAsync(b => b.flat_id == flatId && b.period == period && !b.is_deleted);

        public Task<bool> HasUnpaidBillsAsync(long flatId) =>
            _db.bills.AnyAsync(b => b.flat_id == flatId && !b.is_deleted);

        public async Task<IEnumerable<(decimal Amount, decimal PaidAmount)>> GetUnpaidBillAmountsAsync(long flatId)
        {
            var rows = await _db.bills
                .Where(b => b.flat_id == flatId && !b.is_deleted)
                .Select(b => new { b.amount, paid = b.paid_amount ?? 0m })
                .ToListAsync();
            return rows.Select(r => (r.amount, r.paid));
        }

        public Task<bool> HasUnpaidBillsExcludingStatusAsync(long flatId, string excludeStatus1, string excludeStatus2) =>
            _db.bills.AnyAsync(b => b.flat_id == flatId
                                 && !b.is_deleted
                                 && b.status_code != excludeStatus1
                                 && b.status_code != excludeStatus2);

        public async Task AddRangeAsync(IEnumerable<BillAddDto> bills)
        {
            var entities = bills.Select(Map).ToList();
            await _db.bills.AddRangeAsync(entities);
            await _db.SaveChangesAsync();
        }

        public async Task AddAsync(BillAddDto bill)
        {
            await _db.bills.AddAsync(Map(bill));
            await _db.SaveChangesAsync();
        }

        public Task SaveChangesAsync() => _db.SaveChangesAsync();

        public async Task<ILookup<long, long>> GetExistingFlatIdsForSocietiesAsync(IReadOnlyCollection<long> societyIds, string period)
        {
            if (societyIds.Count == 0)
                return Enumerable.Empty<(long SocietyId, long FlatId)>().ToLookup(x => x.SocietyId, x => x.FlatId);

            var rows = await _db.bills
                .AsNoTracking()
                .Where(b => societyIds.Contains(b.society_id) && b.period == period && !b.is_deleted)
                .Select(b => new { b.society_id, b.flat_id })
                .ToListAsync();

            return rows.ToLookup(r => r.society_id, r => r.flat_id);
        }

        public async Task<decimal> GetOutstandingByFlatIdAsync(long flatId) =>
            await _db.bills
                .Where(b => b.flat_id == flatId && !b.is_deleted
                         && b.status_code != BillStatusCodes.Cancelled
                         && (b.amount - (b.paid_amount ?? 0)) > 0)
                .SumAsync(b => (decimal?)(b.amount - (b.paid_amount ?? 0))) ?? 0m;

        public async Task<decimal> GetTotalChargesByFlatIdAsync(long flatId) =>
            await _db.bills
                .Where(b => b.flat_id == flatId && !b.is_deleted)
                .SumAsync(b => (decimal?)b.amount) ?? 0m;

        private static bill Map(BillAddDto b) => new()
        {
            society_id   = b.SocietyId,
            flat_id      = b.FlatId,
            period       = b.Period,
            amount       = b.Amount,
            status_code  = b.StatusCode,
            generated_by = b.GeneratedBy,
            generated_at = b.GeneratedAt,
            created_at   = b.CreatedAt,
            is_deleted   = false,
            source       = b.Source
        };
    }
}
