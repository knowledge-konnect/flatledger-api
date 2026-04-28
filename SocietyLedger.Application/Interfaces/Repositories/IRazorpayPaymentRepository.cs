using SocietyLedger.Domain.Entities;

namespace SocietyLedger.Application.Interfaces.Repositories
{
    public interface IPaymentRepository
    {
        Task<Payment?> GetByRazorpayOrderIdAsync(string orderId);
        Task<Payment?> GetByRazorpayPaymentIdAsync(string paymentId);
        Task<Payment?> GetPendingSubscriptionPaymentByUserIdAsync(long userId);
        Task AddAsync(Payment payment);
        Task UpdateAsync(Payment payment);
        Task SaveChangesAsync();
        /// <summary>
        /// Acquires a PostgreSQL advisory lock for the duration of <paramref name="action"/>,
        /// then releases it. Used to serialise concurrent payment verification and webhook processing.
        /// </summary>
        Task ExecuteWithAdvisoryLockAsync(long lockKey, Func<Task> action);
    }
}