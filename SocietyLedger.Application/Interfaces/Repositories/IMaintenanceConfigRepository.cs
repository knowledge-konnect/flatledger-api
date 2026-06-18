using SocietyLedger.Application.DTOs.MaintenanceConfig;

namespace SocietyLedger.Application.Interfaces.Repositories
{
    public interface IMaintenanceConfigRepository
    {
        /// <summary>Returns the config for a society, or null if it doesn't exist yet.</summary>
        Task<MaintenanceConfigResponse?> GetBySocietyIdAsync(long societyId);

        /// <summary>Creates or updates the maintenance config for a society (upsert).</summary>
        Task UpsertAsync(long societyId, Guid societyPublicId, SaveMaintenanceConfigRequest request, long changedByUserId);

        /// <summary>
        /// Returns a dictionary of societyId → default monthly charge for the given society IDs.
        /// Missing/unconfigured societies are omitted from the result.
        /// </summary>
        Task<IReadOnlyDictionary<long, decimal>> GetDefaultChargesBySocietyIdsAsync(IReadOnlyCollection<long> societyIds);
    }
}
