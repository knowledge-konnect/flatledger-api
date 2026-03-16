using Microsoft.EntityFrameworkCore;
using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Application.Interfaces.Services.Admin;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Shared;

namespace SocietyLedger.Infrastructure.Services.Admin
{
    public class AdminPaymentService : IAdminPaymentService
    {
        private readonly AppDbContext _db;
        public AdminPaymentService(AppDbContext db) { _db = db; }

        public async Task<PagedResult<AdminPaymentDto>> GetPaymentsAsync(int page, int pageSize, long? societyId = null, string? paymentType = null, DateTime? from = null, DateTime? to = null)
        {
            var query = _db.payments.AsNoTracking().Where(p => !p.is_deleted);

            if (societyId.HasValue)
                query = query.Where(p => p.society_id == societyId);
            if (!string.IsNullOrWhiteSpace(paymentType))
                query = query.Where(p => p.payment_type == paymentType);
            if (from.HasValue)
                query = query.Where(p => p.created_at >= from.Value);
            if (to.HasValue)
                query = query.Where(p => p.created_at <= to.Value);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(p => p.created_at)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new AdminPaymentDto
                {
                    Id = p.id,
                    PublicId = p.public_id,
                    SocietyId = p.society_id,
                    BillId = p.bill_id,
                    FlatId = p.flat_id,
                    Amount = p.amount,
                    DatePaid = p.date_paid,
                    ModeCode = p.mode_code,
                    Reference = p.reference,
                    PaymentType = p.payment_type,
                    RazorpayPaymentId = p.razorpay_payment_id,
                    VerifiedAt = p.verified_at,
                    IsDeleted = p.is_deleted,
                    CreatedAt = p.created_at
                })
                .ToListAsync();

            return new PagedResult<AdminPaymentDto>(items, total, page, pageSize);
        }

        public async Task<AdminPaymentDto?> GetPaymentByIdAsync(long id)
        {
            return await _db.payments
                .AsNoTracking()
                .Where(p => p.id == id && !p.is_deleted)
                .Select(p => new AdminPaymentDto
                {
                    Id = p.id,
                    PublicId = p.public_id,
                    SocietyId = p.society_id,
                    BillId = p.bill_id,
                    FlatId = p.flat_id,
                    Amount = p.amount,
                    DatePaid = p.date_paid,
                    ModeCode = p.mode_code,
                    Reference = p.reference,
                    PaymentType = p.payment_type,
                    RazorpayPaymentId = p.razorpay_payment_id,
                    VerifiedAt = p.verified_at,
                    IsDeleted = p.is_deleted,
                    CreatedAt = p.created_at
                })
                .FirstOrDefaultAsync();
        }
    }
}
