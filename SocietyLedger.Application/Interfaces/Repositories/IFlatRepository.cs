using SocietyLedger.Application.DTOs.Dashboard;
using SocietyLedger.Domain.Entities;

namespace SocietyLedger.Application.Interfaces.Repositories
{
    public interface IFlatRepository
    {
        Task<IEnumerable<Flat>> GetBySocietyIdAsync(long societyId);
        Task<Flat?> GetByPublicIdAsync(Guid publicId, long societyId);
        Task<Flat?> GetByFlatNoAndSocietyAsync(string flatNo, long societyId);
        Task<Flat?> GetByEmailAndSocietyAsync(string email, long societyId);
        Task<Flat?> GetByMobileAndSocietyAsync(string mobile, long societyId);
        Task AddAsync(Flat entity);
        Task<IEnumerable<Flat>> BulkAddAsync(IEnumerable<Flat> flats);
        Task UpdateAsync(Flat entity, long societyId);
        Task DeleteByPublicIdAsync(Guid publicId, long societyId);
        Task SaveChangesAsync();
        Task<IEnumerable<FlatStatus>> GetAllAsync();
        Task<FlatStatus?> GetByCodeAsync(string code);

        /// <summary>Returns flat occupancy counts for a society (SQL-side aggregation).</summary>
        Task<FlatSummaryDto> GetFlatSummaryAsync(long societyId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns slim billing projections for all active flats across the given societies.
        /// Used by the scheduled billing job to avoid per-society queries (N+1).
        /// </summary>
        Task<IReadOnlyList<FlatBillingInfo>> GetActiveFlatsBySocietyIdsAsync(IReadOnlyCollection<long> societyIds);
    }

    /// <summary>Slim flat projection used by the billing job.</summary>
    public record FlatBillingInfo(long SocietyId, long FlatId, decimal MaintenanceAmount);
}
