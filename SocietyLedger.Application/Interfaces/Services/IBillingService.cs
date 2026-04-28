using SocietyLedger.Application.DTOs.Billing;

namespace SocietyLedger.Application.Interfaces.Services
{
    public interface IBillingService
    {
        /// <summary>
        /// Generates a bill for a single flat for the given month if it does not already exist.
        /// </summary>
        Task GenerateBillForFlatAsync(Guid flatPublicId, long userId, DateTime billingMonth);

        /// <summary>
        /// Generates a bill for a single flat for the current UTC month.
        /// Convenience overload so callers don't need to construct the billing month date.
        /// </summary>
        Task GenerateBillForFlatCurrentMonthAsync(Guid flatPublicId, long userId);

        /// <summary>
        /// Generates one bill per active flat for the given period.
        /// Throws ConflictException if bills already exist for that society + period.
        /// Throws NotFoundException if society has no active flats.
        /// Used by the manual per-society endpoint.
        /// </summary>
        Task<GenerateBillsResponse> GenerateBillsAsync(long userId, string period);

        /// <summary>
        /// Returns whether bills have already been generated for the current calendar month.
        /// </summary>
        Task<BillingStatusResponse> GetBillingStatusAsync(long userId);

        /// <summary>
        /// Generates monthly maintenance bills for ALL active societies in the platform.
        /// Intended to be called by the background service (1st of every month) and the
        /// manual admin trigger endpoint. Defaults to the current UTC month when no date is given.
        /// </summary>
        Task<BillingResult> GenerateMonthlyBillsAsync(DateTime? billingMonth = null);
    }
}
