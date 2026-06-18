using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;

namespace SocietyLedger.Api.BackgroundServices
{
    /// <summary>
    /// Hosted service that runs once on application startup to detect and recover missed monthly
    /// billing runs. Uses a configurable lookback window and each society's onboarding_date to
    /// avoid generating retroactive bills for periods that predate a society's registration.
    /// </summary>
    public sealed class BillingCatchupService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BillingCatchupService> _logger;
        private readonly int _lookbackMonths;

        public BillingCatchupService(
            IServiceProvider serviceProvider,
            ILogger<BillingCatchupService> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _lookbackMonths = configuration.GetValue<int>("BackgroundServices:BillingCatchupLookbackMonths", 3);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                await RunCatchupAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // Safety net: the service must never throw out of StartAsync so the application
                // continues to start normally even if something unexpected slips through.
                _logger.LogCritical(ex,
                    "BillingCatchupService encountered an unexpected top-level error during startup. " +
                    "Application will continue to start normally.");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        // ------------------------------------------------------------------ //
        // Internal implementation
        // ------------------------------------------------------------------ //

        private async Task RunCatchupAsync(CancellationToken cancellationToken)
        {
            if (_lookbackMonths == 0)
            {
                _logger.LogWarning(
                    "Billing catch-up is disabled (LookbackMonths=0). Skipping startup catch-up.");
                return;
            }

            var utcNow = DateTime.UtcNow;
            var periods = BuildPeriodWindow(utcNow, _lookbackMonths);

            _logger.LogInformation(
                "BillingCatchupService starting. LookbackMonths={LookbackMonths} PeriodsEvaluated={Periods}",
                _lookbackMonths, string.Join(", ", periods));

            // Load all active societies with their onboarding dates in one query.
            IReadOnlyDictionary<long, DateOnly> onboardingDates;
            Dictionary<long, HashSet<string>> billedPeriodsPerSociety;
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var societyRepo = scope.ServiceProvider.GetRequiredService<ISocietyRepository>();
                var billRepo    = scope.ServiceProvider.GetRequiredService<IBillRepository>();

                onboardingDates = await societyRepo.GetAllActiveOnboardingDatesAsync();

                if (onboardingDates.Count == 0)
                {
                    _logger.LogInformation("BillingCatchupService: no active societies found. Skipping.");
                    return;
                }

                var societyIds = onboardingDates.Keys.ToList();
                billedPeriodsPerSociety = new Dictionary<long, HashSet<string>>(societyIds.Count);

                foreach (var period in periods)
                {
                    var existing = await billRepo.GetExistingFlatIdsForSocietiesAsync(societyIds, period);
                    foreach (var societyId in existing.Select(g => g.Key).Distinct())
                    {
                        if (!billedPeriodsPerSociety.TryGetValue(societyId, out var set))
                        {
                            set = new HashSet<string>();
                            billedPeriodsPerSociety[societyId] = set;
                        }
                        set.Add(period);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex,
                    "BillingCatchupService failed while loading society/bill data. " +
                    "Catch-up aborted. Application will continue to start normally.");
                return;
            }
            var missedPeriods = new List<string>();

            foreach (var period in periods)
            {
                var periodStart = DateOnly.ParseExact(period, "yyyy-MM",
                    System.Globalization.CultureInfo.InvariantCulture);

                // Any society that was onboarded on or before this period and has no bills for it.
                var societiesWithMissedBills = onboardingDates
                    .Where(kv => kv.Value <= periodStart)
                    .Where(kv => !billedPeriodsPerSociety.TryGetValue(kv.Key, out var billed)
                                 || !billed.Contains(period))
                    .Select(kv => kv.Key)
                    .ToList();

                if (societiesWithMissedBills.Count > 0)
                {
                    _logger.LogWarning(
                        "BillingCatchupService detected missed billing period. " +
                        "Period={Period} MissingSocieties={Count} Reason=NoBillsFoundForEligibleSocieties",
                        period, societiesWithMissedBills.Count);
                    missedPeriods.Add(period);
                }
            }

            if (missedPeriods.Count == 0)
            {
                _logger.LogInformation(
                    "BillingCatchupService: no missed billing periods detected. " +
                    "PeriodsChecked={PeriodsChecked}",
                    periods.Count);
                return;
            }

            // Process missed periods oldest-first.
            int periodsRecovered = 0;
            int totalBillsCreated = 0;

            foreach (var period in missedPeriods)
            {
                try
                {
                    var billingMonth = DateTime.ParseExact(
                        period, "yyyy-MM",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None);
                    billingMonth = new DateTime(billingMonth.Year, billingMonth.Month, 1, 0, 0, 0, DateTimeKind.Utc);

                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                    using var scope = _serviceProvider.CreateScope();
                    var billingService = scope.ServiceProvider.GetRequiredService<IBillingService>();

                    // GenerateMonthlyBillsAsync is already idempotent — it skips flats that
                    // already have a bill, so societies that were billed correctly are unaffected.
                    var result = await billingService.GenerateMonthlyBillsAsync(billingMonth, source: "catchup-startup");

                    stopwatch.Stop();

                    _logger.LogInformation(
                        "BillingCatchupService completed catch-up job. " +
                        "Period={Period} BillsCreated={BillsCreated} BillsSkipped={BillsSkipped} ExecutionTime={ExecutionTime}",
                        period, result.BillsCreated, result.BillsSkipped, stopwatch.Elapsed);

                    periodsRecovered++;
                    totalBillsCreated += result.BillsCreated;
                }
                catch (Exception ex)
                {
                    // Fault isolation — log and continue to the next period.
                    _logger.LogCritical(ex,
                        "BillingCatchupService catch-up job failed. " +
                        "Period={Period} Action=ContinuingToNextPeriod",
                        period);
                }
            }

            _logger.LogInformation(
                "BillingCatchupService completed. " +
                "PeriodsChecked={PeriodsChecked} PeriodsRecovered={PeriodsRecovered} TotalBillsCreated={TotalBillsCreated}",
                periods.Count, periodsRecovered, totalBillsCreated);
        }

        /// <summary>
        /// Returns the N calendar months immediately preceding <paramref name="utcNow"/>'s current
        /// month plus the current month itself as <c>yyyy-MM</c> strings in ascending
        /// (oldest-first) order, so that societies registered in the current month also get billed.
        /// </summary>
        internal IReadOnlyList<string> BuildPeriodWindow(DateTime utcNow, int lookbackMonths)
        {
            var currentMonth = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var periods = new List<string>(lookbackMonths + 1);

            for (int i = lookbackMonths; i >= 0; i--)
            {
                periods.Add(currentMonth.AddMonths(-i).ToString("yyyy-MM"));
            }

            return periods;
        }
    }
}
