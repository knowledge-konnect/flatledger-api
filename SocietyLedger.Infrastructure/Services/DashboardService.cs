using Microsoft.Extensions.Logging;
using SocietyLedger.Application.DTOs.Dashboard;
using SocietyLedger.Application.Interfaces.Repositories;
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
        /// No-op — retained for interface compatibility. Dashboard data is now always
        /// fetched live from the DB so there is nothing to invalidate.
        /// </summary>
        void InvalidateDashboardCache(long societyId);
    }

    public class DashboardService : IDashboardService
    {
        private readonly IDashboardRepository _dashboardRepository;
        private readonly IFlatRepository _flatRepo;
        private readonly IUserContext _userContext;
        private readonly ILogger<DashboardService> _logger;

        public DashboardService(
            IDashboardRepository dashboardRepository,
            IFlatRepository flatRepo,
            IUserContext userContext,
            ILogger<DashboardService> logger)
        {
            _dashboardRepository = dashboardRepository;
            _flatRepo = flatRepo;
            _userContext = userContext;
            _logger = logger;
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

            try
            {
                var data = await _dashboardRepository.GetDashboardDataAsync(
                    societyId, startDate, endDate, cancellationToken);

                data.FlatSummary = await _flatRepo.GetFlatSummaryAsync(societyId, cancellationToken);

                return data;
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching dashboard data for societyId: {SocietyId}", societyId);
                throw;
            }
        }

        /// <inheritdoc />
        public void InvalidateDashboardCache(long societyId)
        {
            // No-op: data is fetched live
        }
    }
}