using Microsoft.EntityFrameworkCore;
using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Application.Interfaces.Services.Admin;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Shared;

namespace SocietyLedger.Infrastructure.Services.Admin
{
    public class AdminBillService : IAdminBillService
    {
        private readonly AppDbContext _db;
        public AdminBillService(AppDbContext db) { _db = db; }

        public async Task<PagedResult<AdminBillDto>> GetBillsAsync(int page, int pageSize, long? societyId = null, string? status = null, string? period = null, DateTime? from = null, DateTime? to = null)
        {
            var query = _db.bills
                .AsNoTracking()
                .Where(b => !b.is_deleted)
                .Join(_db.societies, b => b.society_id, s => s.id, (b, s) => new { b, SocietyName = s.name })
                .Join(_db.flats,     x => x.b.flat_id,   f => f.id, (x, f) => new { x.b, x.SocietyName, FlatNo = f.flat_no });

            if (societyId.HasValue)
                query = query.Where(x => x.b.society_id == societyId);
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(x => x.b.status_code == status);
            if (!string.IsNullOrWhiteSpace(period))
                query = query.Where(x => x.b.period == period);
            if (from.HasValue)
                query = query.Where(x => x.b.generated_at >= from.Value);
            if (to.HasValue)
                query = query.Where(x => x.b.generated_at <= to.Value);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(x => x.b.generated_at)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new AdminBillDto
                {
                    Id = x.b.id,
                    PublicId = x.b.public_id,
                    SocietyId = x.b.society_id,
                    SocietyName = x.SocietyName,
                    FlatId = x.b.flat_id,
                    FlatNo = x.FlatNo,
                    Period = x.b.period,
                    Amount = x.b.amount,
                    DueDate = x.b.due_date,
                    StatusCode = x.b.status_code,
                    PaidAmount = x.b.paid_amount,
                    BalanceAmount = x.b.balance_amount,
                    GeneratedAt = x.b.generated_at,
                    IsDeleted = x.b.is_deleted
                })
                .ToListAsync();

            return new PagedResult<AdminBillDto>(items, total, page, pageSize);
        }
    }
}
