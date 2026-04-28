using Microsoft.EntityFrameworkCore;
using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Application.Interfaces.Services.Admin;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Shared;

namespace SocietyLedger.Infrastructure.Services.Admin
{
    public class AdminInvoiceService : IAdminInvoiceService
    {
        private const int MaxPageSize = 200;
        private readonly AppDbContext _db;
        public AdminInvoiceService(AppDbContext db) { _db = db; }

        public async Task<PagedResult<AdminInvoiceDto>> GetInvoicesAsync(int page, int pageSize, long? userId = null, string? status = null, string? invoiceType = null, DateTime? from = null, DateTime? to = null)
        {
            pageSize = Math.Min(pageSize, MaxPageSize);
            var query = _db.invoices
                .AsNoTracking()
                .Join(_db.users, inv => inv.user_id, u => u.id,
                      (inv, u) => new { inv, UserName = u.name });

            if (userId.HasValue)
                query = query.Where(x => x.inv.user_id == userId);
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(x => x.inv.status == status);
            if (!string.IsNullOrWhiteSpace(invoiceType))
                query = query.Where(x => x.inv.invoice_type == invoiceType);
            if (from.HasValue)
                query = query.Where(x => x.inv.created_at >= from.Value);
            if (to.HasValue)
                query = query.Where(x => x.inv.created_at <= to.Value);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(x => x.inv.created_at)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new AdminInvoiceDto
                {
                    Id = x.inv.id,
                    UserId = x.inv.user_id,
                    UserName = x.UserName,
                    SubscriptionId = x.inv.subscription_id,
                    InvoiceNumber = x.inv.invoice_number,
                    InvoiceType = x.inv.invoice_type,
                    Amount = x.inv.amount,
                    TaxAmount = x.inv.tax_amount,
                    TotalAmount = x.inv.total_amount,
                    Currency = x.inv.currency,
                    Status = x.inv.status,
                    PeriodStart = x.inv.period_start,
                    PeriodEnd = x.inv.period_end,
                    DueDate = x.inv.due_date,
                    PaidDate = x.inv.paid_date,
                    PaymentMethod = x.inv.payment_method,
                    PaymentReference = x.inv.payment_reference,
                    CreatedAt = x.inv.created_at
                })
                .ToListAsync();

            return new PagedResult<AdminInvoiceDto>(items, total, page, pageSize);
        }
    }
}
