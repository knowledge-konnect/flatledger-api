using Microsoft.EntityFrameworkCore;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Persistence.Entities;

namespace SocietyLedger.Infrastructure.Persistence.Repositories
{
    public class PaymentRepository : IPaymentRepository
    {
        private readonly AppDbContext _db;

        public PaymentRepository(AppDbContext db)
        {
            _db = db;
        }

        public async Task<Payment?> GetPendingSubscriptionPaymentByUserIdAsync(long userId)
        {
            var efPayment = await _db.payments
                .AsNoTracking()
                .Where(p => p.payment_type == PaymentTypeCodes.Subscription
                         && p.razorpay_payment_id == null
                         && p.recorded_by == userId   // was incorrectly p.society_id == userId
                         && !p.is_deleted)
                .OrderByDescending(p => p.created_at)
                .FirstOrDefaultAsync();
            return efPayment?.ToDomain();
        }
        public async Task<Payment?> GetByRazorpayOrderIdAsync(string orderId)
        {
            var efPayment = await _db.payments
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.razorpay_order_id == orderId && !p.is_deleted);

            return efPayment?.ToDomain();
        }

        public async Task<Payment?> GetByRazorpayPaymentIdAsync(string paymentId)
        {
            var efPayment = await _db.payments
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.razorpay_payment_id == paymentId && !p.is_deleted);

            return efPayment?.ToDomain();
        }

        public async Task AddAsync(Payment payment)
        {
            var entity = payment.ToEntity();
            await _db.payments.AddAsync(entity);
        }

        public async Task UpdateAsync(Payment payment)
        {
            // Update only mutable fields on the tracked entity.
            // This avoids overwriting immutable columns like created_at/public_id
            // when the incoming domain object is detached or partially populated.
            var entity = await _db.payments.FirstOrDefaultAsync(p => p.id == payment.Id && !p.is_deleted);
            if (entity == null) return;

            entity.date_paid = payment.DatePaid;
            entity.reference = payment.Reference;
            entity.razorpay_payment_id = payment.RazorpayPaymentId;
            entity.razorpay_signature = payment.RazorpaySignature;
            entity.verified_at = payment.VerifiedAt;
        }

        public async Task SaveChangesAsync()
        {
            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// Acquires a session-level PostgreSQL advisory lock, runs the action, then releases it.
        /// Serialises concurrent VerifyPaymentAsync + ProcessWebhookAsync calls for the same order.
        /// </summary>
        public async Task ExecuteWithAdvisoryLockAsync(long lockKey, Func<Task> action)
        {
            await _db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({lockKey})");
            try
            {
                await action();
            }
            finally
            {
                await _db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({lockKey})");
            }
        }
    }
}