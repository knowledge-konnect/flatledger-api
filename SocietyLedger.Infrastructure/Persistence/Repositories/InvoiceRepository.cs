using Microsoft.EntityFrameworkCore;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Persistence.Entities;

namespace SocietyLedger.Infrastructure.Persistence.Repositories
{
    public class InvoiceRepository : IInvoiceRepository
    {
        private readonly AppDbContext _db;

        public InvoiceRepository(AppDbContext db)
        {
            _db = db;
        }

        public async Task<Invoice?> GetByIdAsync(Guid id)
        {
            var efInvoice = await _db.invoices
                .Include(i => i.subscription)
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.id == id);

            return efInvoice?.ToDomain();
        }

        public async Task<IEnumerable<Invoice>> GetByUserIdAsync(long userId)
        {
            var efInvoices = await _db.invoices
                .Include(i => i.subscription)
                .Where(i => i.user_id == userId)
                .OrderByDescending(i => i.created_at)
                .AsNoTracking()
                .ToListAsync();

            return efInvoices.Select(i => i.ToDomain());
        }

        public async Task CreateAsync(Invoice invoice)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                // Acquire a transaction-level advisory lock keyed by year+month so concurrent
                // invoice creations for the same month serialise here.  The lock is released
                // automatically when the transaction commits or rolls back.
                var now = DateTime.UtcNow;
                var lockKey = (long)((uint)now.Year << 12 | (uint)now.Month);
                await _db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({lockKey})");

                // Generate the number inside the lock so the read and the insert are atomic.
                invoice.InvoiceNumber = await GenerateNextInvoiceNumberAsync(now);

                var efInvoice = invoice.ToEntity();
                efInvoice.created_at = DateTime.UtcNow;
                efInvoice.updated_at = DateTime.UtcNow;
                _db.invoices.Add(efInvoice);
                await _db.SaveChangesAsync();

                await tx.CommitAsync();
                invoice.Id = efInvoice.id;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // Internal helper — always called while the advisory lock is held.
        private async Task<string> GenerateNextInvoiceNumberAsync(DateTime now)
        {
            var year = now.Year;
            var month = now.Month.ToString("D2");
            var prefix = $"INV-{year}{month}";

            var lastNumber = await _db.invoices
                .Where(i => i.invoice_number.StartsWith(prefix))
                .OrderByDescending(i => i.invoice_number)
                .Select(i => i.invoice_number)
                .FirstOrDefaultAsync();

            int next = 1;
            if (lastNumber != null)
            {
                var parts = lastNumber.Split('-');
                if (parts.Length == 3 && int.TryParse(parts[2], out int last))
                    next = last + 1;
            }

            return $"{prefix}-{next:D4}";
        }

        public async Task UpdateAsync(Invoice invoice)
        {
            // Fetch the tracked entity and update only the fields that PayInvoiceAsync mutates.
            // Using _db.invoices.Update(detachedEntity) marks every column dirty and can
            // overwrite fields (e.g. invoice_number) that were not loaded into the domain model.
            var efInvoice = await _db.invoices.FirstOrDefaultAsync(i => i.id == invoice.Id);
            if (efInvoice == null) return;

            efInvoice.status           = invoice.Status;
            efInvoice.paid_date        = invoice.PaidDate;
            efInvoice.payment_method   = invoice.PaymentMethod;
            efInvoice.payment_reference = invoice.PaymentReference;
            efInvoice.updated_at       = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        public async Task<string> GenerateInvoiceNumberAsync()
            => await GenerateNextInvoiceNumberAsync(DateTime.UtcNow);
    }
}