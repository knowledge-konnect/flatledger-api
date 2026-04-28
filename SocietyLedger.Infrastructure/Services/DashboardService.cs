using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SocietyLedger.Application.DTOs.Dashboard;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Persistence.Repositories;
using SocietyLedger.Infrastructure.Services.Common;

namespace SocietyLedger.Infrastructure.Services
{
    public interface IDashboardService
    {
        /// <summary>
        /// Gets dashboard data by userId — resolves societyId internally.
        /// Use this from endpoints so they don't need a repo dependency.
        /// </summary>
        Task<DashboardResponseDto> GetDashboardDataAsync(
            long userId,
            DateTime? startDate = null,
            DateTime? endDate = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets dashboard data directly by societyId.
        /// Used by internal callers that already have societyId.
        /// </summary>
        Task<DashboardResponseDto> GetDashboardDataBySocietyAsync(
            long societyId,
            DateTime? startDate = null,
            DateTime? endDate = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Invalidates all cached dashboard entries for a society.
        /// Call after any mutation that affects dashboard totals (bills, expenses, payments).
        /// </summary>
        void InvalidateDashboardCache(long societyId);
    }

    public class DashboardService : IDashboardService
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(3);

        private readonly IDashboardRepository _dashboardRepository;
        private readonly IUserContext _userContext;
        private readonly IMemoryCache _cache;
        private readonly ILogger<DashboardService> _logger;
        private readonly AppDbContext _db;

        public DashboardService(
            IDashboardRepository dashboardRepository,
            IUserContext userContext,
            IMemoryCache cache,
            ILogger<DashboardService> logger,
            AppDbContext db)
        {
            _dashboardRepository = dashboardRepository;
            _userContext = userContext;
            _cache = cache;
            _logger = logger;
            _db = db;
        }

        public async Task<DashboardResponseDto> GetDashboardDataAsync(
            long userId,
            DateTime? startDate = null,
            DateTime? endDate = null,
            CancellationToken cancellationToken = default)
        {
            var (_, societyId) = await _userContext.GetUserContextAsync(userId);
            return await GetDashboardDataBySocietyAsync(societyId, startDate, endDate, cancellationToken);
        }

        public async Task<DashboardResponseDto> GetDashboardDataBySocietyAsync(
            long societyId,
            DateTime? startDate = null,
            DateTime? endDate = null,
            CancellationToken cancellationToken = default)
        {
            if (societyId <= 0)
                throw new ArgumentException("Society ID must be greater than 0", nameof(societyId));

            var cacheKey = BuildCacheKey(societyId, startDate, endDate);

            if (_cache.TryGetValue(cacheKey, out DashboardResponseDto? cached) && cached is not null)
            {
                _logger.LogDebug(
                    "Dashboard cache HIT for societyId: {SocietyId}, key: {CacheKey}",
                    societyId, cacheKey);
                return cached;
            }

            try
            {
                var data = await _dashboardRepository.GetDashboardDataAsync(
                    societyId, startDate, endDate, cancellationToken);

                data.FlatSummary = await GetFlatSummaryAsync(societyId, cancellationToken);

                _cache.Set(cacheKey, data, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheDuration,
                    // Evict immediately under memory pressure
                    Priority = CacheItemPriority.Normal
                });

                _logger.LogInformation(
                    "Dashboard data fetched and cached for societyId: {SocietyId} (TTL: {TTL}s)",
                    societyId, CacheDuration.TotalSeconds);

                return data;
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error fetching dashboard data for societyId: {SocietyId}",
                    societyId);
                throw;
            }
        }

        /// <summary>
        /// Computes flat occupancy counts for the society. Not date-range dependent.
        /// </summary>
        private async Task<FlatSummaryDto> GetFlatSummaryAsync(long societyId, CancellationToken cancellationToken)
        {
            // Use SQL-side aggregation so no flat rows are transferred to the application.
            return await _db.flats
                .Where(f => f.society_id == societyId && !f.is_deleted)
                .GroupBy(_ => 1)
                .Select(g => new FlatSummaryDto
                {
                    Total           = g.Count(),
                    Occupied        = g.Count(f => f.status != null && f.status.code == FlatStatusCodes.OwnerOccupied),
                    Vacant          = g.Count(f => f.status != null && f.status.code == FlatStatusCodes.Vacant),
                    Rented          = g.Count(f => f.status != null && f.status.code == FlatStatusCodes.TenantOccupied),
                    ZeroAmountCount = g.Count(f => f.maintenance_amount == 0)
                })
                .FirstOrDefaultAsync(cancellationToken) ?? new FlatSummaryDto();
        }

        /// <inheritdoc />
        public void InvalidateDashboardCache(long societyId)
        {
            var versionKey = $"dashboard-version:{societyId}";
            var current = _cache.Get<long>(versionKey);
            _cache.Set(versionKey, current + 1, new MemoryCacheEntryOptions
            {
                Priority = CacheItemPriority.NeverRemove
            });

            _logger.LogDebug(
                "Dashboard cache invalidated for societyId={SocietyId} (version={Version})",
                societyId, current + 1);
        }

        /// <summary>
        /// Builds a deterministic, version-stamped cache key scoped to the society and optional
        /// date range. Bumping the version via <see cref="InvalidateDashboardCache"/> makes all
        /// previously cached keys for that society unreachable without an explicit Remove loop.
        /// </summary>
        private string BuildCacheKey(long societyId, DateTime? startDate, DateTime? endDate)
        {
            var version = _cache.GetOrCreate($"dashboard-version:{societyId}", e =>
            {
                e.Priority = CacheItemPriority.NeverRemove;
                return 0L;
            });

            var start = startDate.HasValue
                ? startDate.Value.Date.ToString("yyyy-MM-dd")
                : "null";

            var end = endDate.HasValue
                ? endDate.Value.Date.ToString("yyyy-MM-dd")
                : "null";

            return $"dashboard:{societyId}:{version}:{start}:{end}";
        }
    }
}
