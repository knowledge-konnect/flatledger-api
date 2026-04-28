using SocietyLedger.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SocietyLedger.Application.Interfaces.Repositories
{
    public interface ISocietyRepository
    {
        /// <summary>
        /// Adds a new Society to persistence (but does not necessarily save changes).
        /// </summary>
        Task AddAsync(Society society);

        /// <summary>
        /// Retrieves a Society by its public Id (UUID).
        /// Returns null if not found.
        /// </summary>
        Task<Society?> GetByPublicIdAsync(Guid publicId);

        /// <summary>
        /// Retrieves a Society by its Id.
        /// Returns null if not found.
        /// </summary>
        Task<Society?> GetByIdAsync(long id);

        /// <summary>
        /// Returns the onboarding date for a society. Returns null if not found.
        /// </summary>
        Task<DateOnly?> GetOnboardingDateAsync(long societyId);

        /// <summary>
        /// Returns the society_id for a given user_id. Returns null if user not found.
        /// </summary>
        Task<long?> GetSocietyIdByUserIdAsync(long userId);

        /// <summary>
        /// Returns the count of active (non-deleted) flats for a society.
        /// </summary>
        Task<int> CountActiveFlatsBySocietyAsync(long societyId);

        /// <summary>
        /// Persists any pending changes in the underlying context.
        /// </summary>
        Task SaveChangesAsync();

        /// <summary>
        /// Returns the IDs of all non-deleted societies. Used by scheduled billing job.
        /// </summary>
        Task<IReadOnlyList<long>> GetAllActiveIdsAsync();
    }
}

