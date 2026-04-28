using Microsoft.Extensions.Logging;
using SocietyLedger.Application.DTOs.Billing;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Services.Common;
using System.Diagnostics;

namespace SocietyLedger.Infrastructure.Services
{
    public sealed class BillingService : IBillingService
    {
        private readonly IBillRepository _billRepo;
        private readonly IFlatRepository _flatRepo;
        private readonly ISocietyRepository _societyRepo;
        private readonly IMaintenanceConfigRepository _maintConfigRepo;
        private readonly IUserContext _userContext;
        private readonly ILogger<BillingService> _logger;
        private readonly IDashboardService _dashboardService;

        public BillingService(
            IBillRepository billRepo,
            IFlatRepository flatRepo,
            ISocietyRepository societyRepo,
            IMaintenanceConfigRepository maintConfigRepo,
            IUserContext userContext,
            ILogger<BillingService> logger,
            IDashboardService dashboardService)
        {
            _billRepo         = billRepo;
            _flatRepo         = flatRepo;
            _societyRepo      = societyRepo;
            _maintConfigRepo  = maintConfigRepo;
            _userContext      = userContext;
            _logger           = logger;
            _dashboardService = dashboardService;
        }

        // ------------------------------------------------------------------ //
        //  Generate bills manually for a given period                          //
        // ------------------------------------------------------------------ //

        public async Task<GenerateBillsResponse> GenerateBillsAsync(long userId, string period)
        {
            var (user, societyId) = await _userContext.GetUserContextAsync(userId);

            if (await _billRepo.ExistsForPeriodAsync(societyId, period))
                throw new ConflictException(
                    $"Bills for period '{period}' have already been generated for this society.");

            var maxAllowedPeriod = DateTime.UtcNow.AddMonths(1).ToString("yyyy-MM");
            if (string.Compare(period, maxAllowedPeriod, StringComparison.Ordinal) > 0)
                throw new ValidationException(
                    $"Period '{period}' is too far in the future. Bills can only be generated up to 1 month ahead (max allowed: '{maxAllowedPeriod}').");

            var defaultCharge = (await _maintConfigRepo.GetDefaultChargesBySocietyIdsAsync(new[] { societyId }))
                                    .GetValueOrDefault(societyId, 0m);

            var flats = await _flatRepo.GetBySocietyIdAsync(societyId);
            var flatList = flats.ToList();

            if (flatList.Count == 0)
                throw new NotFoundException("Flats", $"No active flats found for society {societyId}.");

            var now = DateTime.UtcNow;

            var bills = flatList.Select(f => new BillAddDto(
                SocietyId:   societyId,
                FlatId:      f.Id,
                Period:      period,
                Amount:      f.MaintenanceAmount > 0 ? f.MaintenanceAmount : defaultCharge,
                StatusCode:  BillStatusCodes.Unpaid,
                GeneratedBy: userId,
                GeneratedAt: now,
                CreatedAt:   now,
                Source:      "manual"
            )).ToList();

            await _billRepo.AddRangeAsync(bills);

            _logger.LogInformation(
                "Bills generated: societyId={SocietyId}, period={Period}, count={Count}, by={UserId}",
                societyId, period, bills.Count, userId);

            _dashboardService.InvalidateDashboardCache(societyId);

            var zeroBillCount = bills.Count(b => b.Amount == 0m);
            var warnings = zeroBillCount > 0
                ? new List<string> { $"{zeroBillCount} flat(s) will be billed \u20b90 — no maintenance amount configured. Update flat or society maintenance config." }
                : null;

            return new GenerateBillsResponse(period, bills.Count, warnings);
        }

        // ------------------------------------------------------------------ //
        //  Billing status check for the current calendar month                //
        // ------------------------------------------------------------------ //

        public async Task<BillingStatusResponse> GetBillingStatusAsync(long userId)
        {
            var societyId = await _userContext.GetSocietyIdAsync(userId);
            var currentMonth = DateTime.UtcNow.ToString("yyyy-MM");
            var count = await _billRepo.CountForPeriodAsync(societyId, currentMonth);

            return new BillingStatusResponse(
                CurrentMonth   : currentMonth,
                IsGenerated    : count > 0,
                GeneratedCount : count
            );
        }

        // ------------------------------------------------------------------ //
        //  Automated monthly bill generation                                   //
        // ------------------------------------------------------------------ //

        public async Task<BillingResult> GenerateMonthlyBillsAsync(DateTime? billingMonth = null)
        {
            var month = billingMonth.HasValue
                ? new DateTime(billingMonth.Value.Year, billingMonth.Value.Month, 1, 0, 0, 0, DateTimeKind.Utc)
                : new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var stopwatch = Stopwatch.StartNew();
            var period    = month.ToString("yyyy-MM");
            int totalFlatsProcessed = 0;
            int billsCreated        = 0;
            int billsSkipped        = 0;
            int failedSocieties     = 0;

            _logger.LogInformation(
                "GenerateMonthlyBillsAsync started. BillingMonth={BillingMonth:yyyy-MM}",
                month);

            var societyIds = await _societyRepo.GetAllActiveIdsAsync();

            if (societyIds.Count == 0)
            {
                stopwatch.Stop();
                _logger.LogWarning("GenerateMonthlyBillsAsync: no active societies found. Period={Period}", period);
                return new BillingResult
                {
                    TotalFlatsProcessed = 0,
                    BillsCreated        = 0,
                    BillsSkipped        = 0,
                    ExecutionTime       = stopwatch.Elapsed,
                    Success             = true,
                    ErrorMessage        = "No active societies with flats found."
                };
            }

            var defaultCharges       = await _maintConfigRepo.GetDefaultChargesBySocietyIdsAsync(societyIds);
            var allFlats             = (await _flatRepo.GetActiveFlatsBySocietyIdsAsync(societyIds))
                                           .ToLookup(f => f.SocietyId);
            var existingBillFlatIds  = await _billRepo.GetExistingFlatIdsForSocietiesAsync(societyIds, period);

            foreach (var societyId in societyIds)
            {
                try
                {
                    var defaultCharge   = defaultCharges.GetValueOrDefault(societyId, 0m);
                    var flats           = allFlats[societyId].ToList();
                    var flatIdsWithBill = existingBillFlatIds[societyId].ToHashSet();

                    totalFlatsProcessed += flats.Count;

                    if (flats.Count == 0)
                    {
                        _logger.LogWarning("Society {SocietyId}: no active flats found, skipping.", societyId);
                        continue;
                    }

                    var now      = DateTime.UtcNow;
                    var newBills = new List<BillAddDto>(flats.Count);

                    foreach (var flat in flats)
                    {
                        if (flatIdsWithBill.Contains(flat.FlatId)) { billsSkipped++; continue; }

                        var amount = flat.MaintenanceAmount > 0 ? flat.MaintenanceAmount : defaultCharge;

                        newBills.Add(new BillAddDto(
                            SocietyId:   societyId,
                            FlatId:      flat.FlatId,
                            Period:      period,
                            Amount:      amount,
                            StatusCode:  BillStatusCodes.Unpaid,
                            GeneratedBy: null,
                            GeneratedAt: now,
                            CreatedAt:   now,
                            Source:      "scheduled"
                        ));
                    }

                    if (newBills.Count > 0)
                    {
                        await _billRepo.AddRangeAsync(newBills);
                        billsCreated += newBills.Count;
                        _dashboardService.InvalidateDashboardCache(societyId);
                    }

                    _logger.LogInformation(
                        "Society {SocietyId}: created={BillsCreated}, skipped={BillsSkipped}, period={Period}.",
                        societyId, newBills.Count, flatIdsWithBill.Count == 0 ? 0 : flats.Count - newBills.Count, period);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Society {SocietyId}: billing failed for period {Period}. Error: {Error}",
                        societyId, period, ex.Message);
                    failedSocieties++;
                }
            }

            stopwatch.Stop();
            var hasFailures = failedSocieties > 0;

            if (hasFailures)
                _logger.LogWarning(
                    "GenerateMonthlyBillsAsync completed with partial failures. Period={Period}, " +
                    "TotalFlatsProcessed={TotalFlatsProcessed}, BillsCreated={BillsCreated}, " +
                    "BillsSkipped={BillsSkipped}, FailedSocieties={FailedSocieties}, Duration={Duration:c}",
                    period, totalFlatsProcessed, billsCreated, billsSkipped, failedSocieties, stopwatch.Elapsed);
            else
                _logger.LogInformation(
                    "GenerateMonthlyBillsAsync completed. Period={Period}, " +
                    "TotalFlatsProcessed={TotalFlatsProcessed}, BillsCreated={BillsCreated}, " +
                    "BillsSkipped={BillsSkipped}, Duration={Duration:c}",
                    period, totalFlatsProcessed, billsCreated, billsSkipped, stopwatch.Elapsed);

            return new BillingResult
            {
                TotalFlatsProcessed = totalFlatsProcessed,
                BillsCreated        = billsCreated,
                BillsSkipped        = billsSkipped,
                FailedSocieties     = failedSocieties,
                ExecutionTime       = stopwatch.Elapsed,
                Success             = !hasFailures,
                ErrorMessage        = hasFailures
                    ? $"{failedSocieties} society(s) failed to process for period {period}. Check logs for details."
                    : null
            };
        }

        public async Task GenerateBillForFlatAsync(Guid flatPublicId, long userId, DateTime billingMonth)
        {
            var period    = billingMonth.ToString("yyyy-MM");
            var societyId = await _userContext.GetSocietyIdAsync(userId);

            var flat = await _flatRepo.GetByPublicIdAsync(flatPublicId, societyId);

            if (flat == null)
                throw new NotFoundException("Flat", $"Flat with public id {flatPublicId} not found or does not belong to this society.");

            if (await _billRepo.ExistsForFlatAndPeriodAsync(flat.Id, period))
                return;

            var defaultCharge = (await _maintConfigRepo.GetDefaultChargesBySocietyIdsAsync(new[] { societyId }))
                                    .GetValueOrDefault(societyId, 0m);

            var amount = flat.MaintenanceAmount > 0 ? flat.MaintenanceAmount : defaultCharge;

            var now = DateTime.UtcNow;
            var newBill = new BillAddDto(
                SocietyId:   societyId,
                FlatId:      flat.Id,
                Period:      period,
                Amount:      amount,
                StatusCode:  BillStatusCodes.Unpaid,
                GeneratedBy: null,
                GeneratedAt: now,
                CreatedAt:   now,
                Source:      "flat-create"
            );

            try
            {
                await _billRepo.AddAsync(newBill);
                _logger.LogInformation("Generated bill for flat {FlatId}, period {Period}", flat.Id, period);
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                _logger.LogDebug("Bill already exists for flat {FlatId} period {Period} — concurrent insert, skipped", flat.Id, period);
            }
        }

        public Task GenerateBillForFlatCurrentMonthAsync(Guid flatPublicId, long userId)
            => GenerateBillForFlatAsync(flatPublicId, userId, DateTime.UtcNow);

        private static bool IsUniqueConstraintViolation(Microsoft.EntityFrameworkCore.DbUpdateException ex)
            => ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true
            || ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true
            || ex.InnerException?.Message.Contains("23505") == true;
    }
}
