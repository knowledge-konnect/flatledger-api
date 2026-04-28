using SocietyLedger.Application.DTOs.Flat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SocietyLedger.Application.Interfaces.Services
{
    public interface IFlatService
    {
        /// <summary>
        /// Get all flats for the society that the given user belongs to.
        /// The service resolves societyId internally — callers only need userId.
        /// </summary>
        Task<IEnumerable<FlatResponseDto>> GetBySocietyAsync(long userId);

        // Legacy overload used by internal service-to-service calls that already have societyId.
        Task<IEnumerable<FlatResponseDto>> GetBySocietyIdAsync(long societyId);
        /// <summary>
        /// Get a flat by its public UUID with tenant isolation.
        /// </summary>
        Task<FlatResponseDto?> GetByPublicIdAsync(Guid publicId, long userId);

        /// <summary>
        /// Create a new flat and return the created flat DTO.
        /// </summary>
        Task<FlatResponseDto> CreateAsync(CreateFlatDto dto, long userId);

        /// <summary>
        /// Bulk create multiple flats in a single operation with transactional integrity.
        /// Validates all items, batch inserts them, and generates bills in parallel (unless skipBilling=true).
        /// Returns succeeded and failed results with individual error messages.
        /// </summary>
        Task<BulkCreateFlatsResponse> BulkCreateAsync(BulkCreateFlatsRequest request, long userId, bool skipBilling = false);

        /// <summary>
        /// Update an existing flat with tenant isolation.
        /// Returns the updated DTO if found, or null if not.
        /// </summary>
        Task<FlatResponseDto?> UpdateAsync(UpdateFlatDto dto, long userId);

        /// <summary>
        /// Delete a flat by its public UUID with tenant isolation.
        /// Returns true if the record was deleted, false if not found.
        /// </summary>
        Task<bool> DeleteByPublicIdAsync(Guid publicId, long userId);

        /// <summary>
        /// Returns all flat statuses suitable for populating a dropdown.
        /// </summary>
        Task<IEnumerable<FlatStatusDto>> GetAllAsync();

        /// <summary>
        /// Get flat ledger with all transactions and running balance.
        /// </summary>
        Task<FlatLedgerResponse> GetFlatLedgerAsync(Guid publicId, long userId, DateTime? startDate = null, DateTime? endDate = null);

        /// <summary>
        /// Get flat financial summary with balances and charges.
        /// </summary>
        Task<FlatFinancialSummaryResponse> GetFlatFinancialSummaryAsync(Guid publicId, long userId);

        /// <summary>
        /// Get financial summaries for multiple flats in a single call.
        /// Only returns summaries for flats belonging to the user's society.
        /// Unknown or cross-society IDs are silently skipped.
        /// </summary>
        Task<BulkFinancialSummaryResponse> GetBulkFinancialSummaryAsync(IEnumerable<Guid> flatPublicIds, long userId);

        /// <summary>
        /// Returns a paginated, filtered, and sorted list of flats for the user's society.
        /// All parameters are optional — omitting them returns the first page of all flats.
        /// </summary>
        Task<PagedFlatsResponse> GetPagedAsync(
            long userId,
            string? search,
            string? statusCode,
            int page,
            int size,
            string sortBy,
            string sortDir);
    }
}
