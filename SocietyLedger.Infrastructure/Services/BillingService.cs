using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SocietyLedger.Application.DTOs.Billing;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Services.Common;
using System.Diagnostics;

namespace SocietyLedger.Infrastructure.Services
{
    public sealed class BillingService : IBillingService
    {
        private readonly AppDbContext _db;
        private readonly IBillRepository _billRepo;
        private readonly IUserContext _userContext;
        private readonly ILogger<BillingService> _logger;
        private readonly IDashboardService _dashboardService;

        public BillingService(
            AppDbContext db,
            IBillRepository billRepo,
            IUserContext userContext,
            ILogger<BillingService> logger,
            IDashboardService dashboardService)
        {
            _db               = db;
            _billRepo         = billRepo;
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

            var maintConfig = await _db.maintenance_configs
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.society_id == societyId);

            var flats = await _db.flats
                .AsNoTracking()
                .Where(f => f.society_id == societyId && !f.is_deleted)
                .Select(f => new { f.id, f.maintenance_amount })
                .ToListAsync();

            if (flats.Count == 0)
                throw new NotFoundException("Flats", $"No active flats found for society {societyId}.");

            var now = DateTime.UtcNow;

            var bills = flats.Select(f => new BillAddDto(
                SocietyId:   societyId,
                FlatId:      f.id,
                Period:      period,
                Amount:      f.maintenance_amount > 0
                                 ? f.maintenance_amount
                                 : (maintConfig?.default_monthly_charge ?? 0m),
                StatusCode:  BillStatusCodes.Unpaid,
                GeneratedBy: userId,
                GeneratedAt: now,
                CreatedAt:   now,
                Source:      "manual"
            )).ToList();

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var entities = bills.Select(b => new SocietyLedger.Infrastructure.Persistence.Entities.bill
                {
                    society_id   = b.SocietyId,
                    flat_id      = b.FlatId,
                    period       = b.Period,
                    amount       = b.Amount,
                    status_code  = b.StatusCode,
                    generated_by = b.GeneratedBy,
                    generated_at = b.GeneratedAt,
                    created_at   = b.CreatedAt,
                    is_deleted   = false,
                    source       = b.Source
                }).ToList();

                await _db.bills.AddRangeAsync(entities);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

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

            var societyIds = await _db.societies
                .Where(s => !s.is_deleted)
                .Select(s => s.id)
                .ToListAsync();

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

            var allMaintConfigs = await _db.maintenance_configs
                .AsNoTracking()
                .Where(c => societyIds.Contains(c.society_id))
                .ToDictionaryAsync(c => c.society_id);

            var allFlats = (await _db.flats
                .AsNoTracking()
                .Where(f => societyIds.Contains(f.society_id) && !f.is_deleted)
                .Select(f => new { f.society_id, f.id, f.maintenance_amount })
                .ToListAsync())
                .ToLookup(f => f.society_id);

            var existingBillFlatIds = (await _db.bills
                .Where(b => societyIds.Contains(b.society_id) && b.period == period && !b.is_deleted)
                .Select(b => new { b.society_id, b.flat_id })
                .ToListAsync())
                .ToLookup(b => b.society_id, b => b.flat_id);

            foreach (var societyId in societyIds)
            {
                try
                {
                    var maintConfig     = allMaintConfigs.GetValueOrDefault(societyId);
                    var flats           = allFlats[societyId].ToList();
                    var flatIdsWithBill = existingBillFlatIds[societyId].ToHashSet();

                    totalFlatsProcessed += flats.Count;

                    if (flats.Count == 0)
                    {
                        _logger.LogWarning("Society {SocietyId}: no active flats found, skipping.", societyId);
                        continue;
                    }

                    var now      = DateTime.UtcNow;
                    var newBills = new List<SocietyLedger.Infrastructure.Persistence.Entities.bill>(flats.Count);

                    foreach (var flat in flats)
                    {
                        if (flatIdsWithBill.Contains(flat.id)) { billsSkipped++; continue; }

                        var amount = flat.maintenance_amount > 0
                            ? flat.maintenance_amount
                            : (maintConfig?.default_monthly_charge ?? 0m);

                        newBills.Add(new SocietyLedger.Infrastructure.Persistence.Entities.bill
                        {
                            society_id   = societyId,
                            flat_id      = flat.id,
                            period       = period,
                            amount       = amount,
                            status_code  = BillStatusCodes.Unpaid,
                            generated_by = null,
                            generated_at = now,
                            created_at   = now,
                            is_deleted   = false,
                            source       = "scheduled"
                        });
                    }

                    if (newBills.Count > 0)
                    {
                        await using var tx = await _db.Database.BeginTransactionAsync();
                        try
                        {
                            await _db.bills.AddRangeAsync(newBills);
                            await _db.SaveChangesAsync();
                            await tx.CommitAsync();
                            billsCreated += newBills.Count;
                            _dashboardService.InvalidateDashboardCache(societyId);
                        }
                        catch
                        {
                            await tx.RollbackAsync();
                            throw;
                        }
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

            var flat = await _db.flats
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.public_id == flatPublicId
                                       && f.society_id == societyId
                                       && !f.is_deleted);

            if (flat == null)
                throw new NotFoundException("Flat", $"Flat with public id {flatPublicId} not found or does not belong to this society.");

            if (await _billRepo.ExistsForFlatAndPeriodAsync(flat.id, period))
                return;

            var maintConfig = await _db.maintenance_configs
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.society_id == flat.society_id);

            var amount = flat.maintenance_amount > 0
                ? flat.maintenance_amount
                : (maintConfig?.default_monthly_charge ?? 0m);

            var now = DateTime.UtcNow;
            var newBill = new SocietyLedger.Infrastructure.Persistence.Entities.bill
            {
                society_id   = flat.society_id,
                flat_id      = flat.id,
                period       = period,
                amount       = amount,
                status_code  = BillStatusCodes.Unpaid,
                generated_by = null,
                generated_at = now,
                created_at   = now,
                is_deleted   = false,
                source       = "flat-create"
            };

            try
            {
                await _db.bills.AddAsync(newBill);
                await _db.SaveChangesAsync();
                _logger.LogInformation("Generated bill for flat {FlatId}, period {Period}", flat.id, period);
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                _logger.LogDebug("Bill already exists for flat {FlatId} period {Period} — concurrent insert, skipped", flat.id, period);
            }
        }

        private static bool IsUniqueConstraintViolation(Microsoft.EntityFrameworkCore.DbUpdateException ex)
            => ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true
            || ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true
            || ex.InnerException?.Message.Contains("23505") == true;
    }
}
