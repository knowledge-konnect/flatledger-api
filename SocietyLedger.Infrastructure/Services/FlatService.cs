using SocietyLedger.Application.DTOs.Flat;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace SocietyLedger.Infrastructure.Services
{
    public class FlatService : IFlatService
    {
        private readonly IFlatRepository _repo;
        private readonly IUserContext _userContext;
        private readonly AppDbContext _db;
        private readonly ILogger<FlatService> _logger;
        private readonly IMaintenanceConfigRepository _maintenanceConfigRepo;
        private readonly IBillingService _billingService;

        public FlatService(
            IFlatRepository repo, 
            IUserContext userContext,
            AppDbContext db,
            ILogger<FlatService> logger,
            IMaintenanceConfigRepository maintenanceConfigRepo,
            IBillingService billingService)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _maintenanceConfigRepo = maintenanceConfigRepo ?? throw new ArgumentNullException(nameof(maintenanceConfigRepo));
            _billingService = billingService ?? throw new ArgumentNullException(nameof(billingService));
        }

        /// <summary>
        /// Returns all flats for the society the given user belongs to.
        /// Resolves societyId internally so endpoints don't need a repo dependency.
        /// </summary>
        public async Task<IEnumerable<FlatResponseDto>> GetBySocietyAsync(long userId)
        {
            var (_, societyId) = await _userContext.GetUserContextAsync(userId);
            return await GetBySocietyIdAsync(societyId);
        }

        /// <summary>
        /// Returns all flats for a society by societyId directly.
        /// Used by internal service-to-service calls that already have societyId.
        /// </summary>
        public async Task<IEnumerable<FlatResponseDto>> GetBySocietyIdAsync(long societyId)
        {
            var list = (await _repo.GetBySocietyIdAsync(societyId)).ToList();
            var flatIds = list.Select(f => f.Id).ToList();
            var outstanding = await ComputeOutstandingByFlatIdAsync(flatIds);
            return list.Select(f => MapToDto(f, outstanding.GetValueOrDefault(f.Id, 0m)));
        }
        /// <summary>
        /// Create a new flat and return the created DTO.
        /// </summary>
        public async Task<FlatResponseDto> CreateAsync(CreateFlatDto dto, long userId)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            var societyId = await _userContext.GetSocietyIdAsync(userId);

            // Check for duplicate flat number in the society
            var existingFlat = await _repo.GetByFlatNoAndSocietyAsync(dto.FlatNo, societyId);
            if (existingFlat != null)
                throw new DuplicateException("flat", "flat number");

            // Check for duplicate email in the same society
            if (!string.IsNullOrWhiteSpace(dto.ContactEmail))
            {
                var existingEmail = await _repo.GetByEmailAndSocietyAsync(dto.ContactEmail, societyId);
                if (existingEmail != null)
                    throw new DuplicateException("flat", "email");
            }

            // Check for duplicate mobile in the same society
            if (!string.IsNullOrWhiteSpace(dto.ContactMobile))
            {
                var existingMobile = await _repo.GetByMobileAndSocietyAsync(dto.ContactMobile, societyId);
                if (existingMobile != null)
                    throw new DuplicateException("flat", "mobile number");
            }

            // Get status by code if provided, otherwise use default
            short? statusId = null;
            if (!string.IsNullOrEmpty(dto.StatusCode))
            {
                var status = await _repo.GetByCodeAsync(dto.StatusCode);
                if (status == null)
                    throw new ValidationException($"Invalid flat status code: {dto.StatusCode}");
                statusId = status.Id;
            }

            var now = DateTime.UtcNow;

            var domain = new Flat
            {
                PublicId = Guid.NewGuid(),
                SocietyId = societyId,
                FlatNo = dto.FlatNo,
                OwnerName = dto.OwnerName,
                ContactMobile = dto.ContactMobile,
                ContactEmail = dto.ContactEmail,
                MaintenanceAmount = dto.MaintenanceAmount ?? 0m,
                StatusId = statusId,
                CreatedAt = now,
                UpdatedAt = now
            };

            await _repo.AddAsync(domain);
            await _repo.SaveChangesAsync();

            _logger.LogInformation("Flat created successfully for FlatNo {FlatNo}", dto.FlatNo);
            return MapToDto(domain);
        }

        /// <summary>
        /// Bulk create multiple flats in a single operation with transactional integrity.
        /// Validates all items, batch inserts them, and generates bills in parallel (unless skipBilling=true).
        /// Returns succeeded and failed results with individual error messages.
        /// </summary>
        public async Task<BulkCreateFlatsResponse> BulkCreateAsync(BulkCreateFlatsRequest request, long userId, bool skipBilling = false)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.Flats == null || request.Flats.Count == 0)
                throw new ValidationException("Flats list cannot be empty");

            if (request.Flats.Count > SocietyLedger.Domain.Constants.ValidationPatterns.MaxBulkFlats)
                throw new ValidationException($"Bulk create is limited to {SocietyLedger.Domain.Constants.ValidationPatterns.MaxBulkFlats} flats per request");

            var societyId = await _userContext.GetSocietyIdAsync(userId);
            var maintenanceConfig = await _maintenanceConfigRepo.GetBySocietyIdAsync(societyId);
            var defaultMaintenanceAmount = maintenanceConfig?.DefaultMonthlyCharge ?? 0m;

            _logger.LogInformation("Bulk create: using maintenance amount {Amount} from config for societyId {SocietyId}", 
                defaultMaintenanceAmount, societyId);

            // Pre-load all existing flat numbers, emails, and mobiles for this society in 3 bulk queries
            // so per-flat validation is done in-memory instead of one DB round-trip per flat.
            var existingFlatNos = await _db.flats
                .Where(f => f.society_id == societyId && !f.is_deleted)
                .Select(f => f.flat_no)
                .ToListAsync();
            var existingFlatNoSet = new HashSet<string>(existingFlatNos, StringComparer.OrdinalIgnoreCase);

            var existingEmails = await _db.flats
                .Where(f => f.society_id == societyId && !f.is_deleted && f.contact_email != null)
                .Select(f => f.contact_email!)
                .ToListAsync();
            var existingEmailSet = new HashSet<string>(existingEmails, StringComparer.OrdinalIgnoreCase);

            var existingMobiles = await _db.flats
                .Where(f => f.society_id == societyId && !f.is_deleted && f.contact_mobile != null)
                .Select(f => f.contact_mobile!)
                .ToListAsync();
            var existingMobileSet = new HashSet<string>(existingMobiles, StringComparer.OrdinalIgnoreCase);

            // Pre-fetch distinct status codes referenced in this request (typically 0-2 values)
            var distinctStatusCodes = request.Flats
                .Where(f => !string.IsNullOrEmpty(f?.StatusCode))
                .Select(f => f!.StatusCode!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var statusCache = new Dictionary<string, SocietyLedger.Domain.Entities.FlatStatus?>(StringComparer.OrdinalIgnoreCase);
            foreach (var code in distinctStatusCodes)
                statusCache[code] = await _repo.GetByCodeAsync(code);

            var succeeded = new List<FlatResponseDto>();
            var failed = new List<BulkFlatFailure>();
            var validFlats = new List<(int Index, string FlatNo, Flat FlatEntity, BulkCreateFlatItemDto OriginalItem)>();

            // Track values accepted in this batch to catch within-request duplicates
            var batchFlatNos   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var batchEmails    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var batchMobiles   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Phase 1: Validate all items upfront (in-memory after bulk pre-load)
            for (int i = 0; i < request.Flats.Count; i++)
            {
                var item = request.Flats[i];
                var flatNo = item?.FlatNo ?? $"(index {i})";

                try
                {
                    if (item == null)
                    {
                        failed.Add(new BulkFlatFailure(i, flatNo, "Flat item is null"));
                        continue;
                    }

                    // Check required fields
                    if (string.IsNullOrWhiteSpace(item.FlatNo))
                    {
                        failed.Add(new BulkFlatFailure(i, flatNo, "FlatNo is required"));
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(item.OwnerName))
                    {
                        failed.Add(new BulkFlatFailure(i, flatNo, "OwnerName is required"));
                        continue;
                    }

                    // Check for duplicate flat number (in DB or within this batch)
                    if (existingFlatNoSet.Contains(item.FlatNo) || !batchFlatNos.Add(item.FlatNo))
                    {
                        failed.Add(new BulkFlatFailure(i, item.FlatNo, "Flat number already exists in this society"));
                        _logger.LogWarning("Bulk flat create: duplicate flat number {FlatNo} at index {Index}", item.FlatNo, i);
                        continue;
                    }

                    // Check for duplicate email if provided (in DB or within this batch)
                    if (!string.IsNullOrWhiteSpace(item.ContactEmail))
                    {
                        if (existingEmailSet.Contains(item.ContactEmail) || !batchEmails.Add(item.ContactEmail))
                        {
                            failed.Add(new BulkFlatFailure(i, item.FlatNo, "Email already exists in this society"));
                            _logger.LogWarning("Bulk flat create: duplicate email {Email} at index {Index}", item.ContactEmail, i);
                            continue;
                        }
                    }

                    // Check for duplicate mobile if provided (in DB or within this batch)
                    if (!string.IsNullOrWhiteSpace(item.ContactMobile))
                    {
                        if (existingMobileSet.Contains(item.ContactMobile) || !batchMobiles.Add(item.ContactMobile))
                        {
                            failed.Add(new BulkFlatFailure(i, item.FlatNo, "Mobile number already exists in this society"));
                            _logger.LogWarning("Bulk flat create: duplicate mobile {Mobile} at index {Index}", item.ContactMobile, i);
                            continue;
                        }
                    }

                    // Resolve status code from pre-loaded cache
                    short? statusId = null;
                    string? statusName = null;

                    if (!string.IsNullOrEmpty(item.StatusCode))
                    {
                        if (!statusCache.TryGetValue(item.StatusCode, out var status) || status == null)
                        {
                            failed.Add(new BulkFlatFailure(i, item.FlatNo, $"Invalid flat status code: {item.StatusCode}"));
                            _logger.LogWarning("Bulk flat create: invalid status code {StatusCode} at index {Index}", item.StatusCode, i);
                            continue;
                        }
                        statusId = status.Id;
                        statusName = status.DisplayName;
                    }

                    var now = DateTime.UtcNow;
                    var flatEntity = new Flat
                    {
                        PublicId = Guid.NewGuid(),
                        SocietyId = societyId,
                        FlatNo = item.FlatNo,
                        OwnerName = item.OwnerName,
                        ContactMobile = item.ContactMobile,
                        ContactEmail = item.ContactEmail,
                        MaintenanceAmount = defaultMaintenanceAmount,
                        StatusId = statusId,
                        StatusName = statusName,
                        CreatedAt = now,
                        UpdatedAt = now
                    };

                    validFlats.Add((i, item.FlatNo, flatEntity, item));
                }
                catch (Exception ex)
                {
                    failed.Add(new BulkFlatFailure(i, flatNo, ex.Message));
                    _logger.LogWarning(ex, "Bulk flat create validation error at index {Index} FlatNo {FlatNo}", i, flatNo);
                }
            }

            // Phase 2: Batch insert all validated flats in a single database operation
            if (validFlats.Count > 0)
            {
                try
                {
                    var createdFlats = await _repo.BulkAddAsync(validFlats.Select(v => v.FlatEntity));
                    var createdList = createdFlats.ToList();

                    for (int i = 0; i < validFlats.Count; i++)
                    {
                        var created = createdList[i];
                        succeeded.Add(MapToDto(created));
                        _logger.LogInformation("Flat {FlatNo} created with ID {PublicId} during bulk operation", 
                            created.FlatNo, created.PublicId);
                    }

                    // Phase 3: Generate bills for all created flats (unless skipBilling=true).
                    // Use limited concurrency and a small retry policy to avoid flooding downstream services
                    // and to be resilient to transient network issues when processing large batches.
                    if (skipBilling)
                    {
                        _logger.LogInformation("Bulk flat create: skipBilling=true, skipping bill generation for {Count} flats", createdList.Count);
                    }
                    else
                    {
                        var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

                        // Process billing sequentially to avoid DbContext concurrency errors.
                        // DbContext is not thread-safe; Task.Run with a shared instance causes
                        // "A second operation was started on this context before a previous one completed".
                        foreach (var flat in createdList)
                        {
                            try
                            {
                                await _billingService.GenerateBillForFlatAsync(flat.PublicId, userId, currentMonth).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Billing generation failed for flat {PublicId} during bulk create (non-fatal)", flat.PublicId);
                            }
                        }
                    }
                }
                catch (Exception batchEx)
                {
                    _logger.LogError(batchEx, "Batch insert failed for {Count} validated flats", validFlats.Count);
                    // Mark all valid flats as failed if batch operation failed
                    foreach (var (index, flatNo, _, _) in validFlats)
                    {
                        failed.Add(new BulkFlatFailure(index, flatNo, 
                            $"Batch database operation failed: {batchEx.Message}"));
                    }
                }
            }

            _logger.LogInformation("Bulk flat create completed: {SucceededCount} succeeded, {FailedCount} failed, SkipBilling={SkipBilling}", 
                succeeded.Count, failed.Count, skipBilling);

            return new BulkCreateFlatsResponse(succeeded, failed);
        }

        
        /// <summary>
        /// Get a flat by its public UUID with tenant isolation.
        /// </summary>
        public async Task<FlatResponseDto?> GetByPublicIdAsync(Guid publicId, long userId)
        {
            var societyId = await _userContext.GetSocietyIdAsync(userId);

            var domain = await _repo.GetByPublicIdAsync(publicId, societyId);
            if (domain == null)
                throw new NotFoundException("Flat", publicId.ToString());
            return MapToDto(domain);
        }

        /// <summary>
        /// Update an existing flat with tenant isolation. Returns the updated DTO if found, or null if not.
        /// </summary>
        public async Task<FlatResponseDto?> UpdateAsync(UpdateFlatDto dto, long userId)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            var societyId = await _userContext.GetSocietyIdAsync(userId);

            var existing = await _repo.GetByPublicIdAsync(dto.PublicId, societyId);
            if (existing == null)
                throw new NotFoundException("Flat", dto.PublicId.ToString());

            // Check for duplicate flat number if changing
            if (dto.FlatNo != null && dto.FlatNo != existing.FlatNo)
            {
                var conflictingFlat = await _repo.GetByFlatNoAndSocietyAsync(dto.FlatNo, existing.SocietyId);
                if (conflictingFlat != null && conflictingFlat.PublicId != existing.PublicId)
                    throw new DuplicateException("flat", "flat number");
            }

            // Check for duplicate email if changing
            if (!string.IsNullOrWhiteSpace(dto.ContactEmail) && dto.ContactEmail != existing.ContactEmail)
            {
                var conflictingEmail = await _repo.GetByEmailAndSocietyAsync(dto.ContactEmail, existing.SocietyId);
                if (conflictingEmail != null && conflictingEmail.PublicId != existing.PublicId)
                    throw new DuplicateException("flat", "email");
            }

            // Check for duplicate mobile if changing
            if (!string.IsNullOrWhiteSpace(dto.ContactMobile) && dto.ContactMobile != existing.ContactMobile)
            {
                var conflictingMobile = await _repo.GetByMobileAndSocietyAsync(dto.ContactMobile, existing.SocietyId);
                if (conflictingMobile != null && conflictingMobile.PublicId != existing.PublicId)
                    throw new DuplicateException("flat", "mobile number");
            }

            // Get status by code if provided
            short? statusId = existing.StatusId;
            if (dto.StatusCode != null)
            {
                var status = await _repo.GetByCodeAsync(dto.StatusCode);
                if (status == null)
                    throw new ValidationException($"Invalid flat status code: {dto.StatusCode}");
                statusId = status.Id;

                // #4 — Cannot mark a flat as vacant while it has outstanding unpaid bills.
                if (status.Code == FlatStatusCodes.Vacant)
                {
                    var hasUnpaid = await _db.bills
                        .AnyAsync(b => b.flat_id == existing.Id
                                    && !b.is_deleted
                                    && b.status_code != BillStatusCodes.Paid
                                    && b.status_code != BillStatusCodes.Cancelled);

                    if (hasUnpaid)
                        throw new ConflictException(
                            $"Cannot mark flat '{existing.FlatNo}' as vacant — it has outstanding unpaid bills. Settle all dues first.");
                }
            }

            existing.FlatNo = dto.FlatNo ?? existing.FlatNo;
            existing.OwnerName = dto.OwnerName ?? existing.OwnerName;
            existing.ContactMobile = dto.ContactMobile ?? existing.ContactMobile;
            existing.ContactEmail = dto.ContactEmail ?? existing.ContactEmail;
            existing.MaintenanceAmount = dto.MaintenanceAmount ?? existing.MaintenanceAmount;
            existing.StatusId = statusId;
            existing.UpdatedAt = DateTime.UtcNow;

            await _repo.UpdateAsync(existing, societyId);
            await _repo.SaveChangesAsync();

            _logger.LogInformation("Flat updated successfully for PublicId {PublicId}", dto.PublicId);
            return MapToDto(existing);
        }
        

        /// <summary>
        /// Delete a flat by its public UUID with tenant isolation. Returns true if deleted, false if not found.
        /// </summary>
        public async Task<bool> DeleteByPublicIdAsync(Guid publicId, long userId)
        {
            var societyId = await _userContext.GetSocietyIdAsync(userId);

            var existing = await _repo.GetByPublicIdAsync(publicId, societyId);
            if (existing == null)
                throw new NotFoundException("Flat", publicId.ToString());

            // #3 — Block deletion when the flat has outstanding unpaid bills.
            var unpaidBills = await _db.bills
                .Where(b => b.flat_id == existing.Id
                         && !b.is_deleted
                         && b.status_code != BillStatusCodes.Paid
                         && b.status_code != BillStatusCodes.Cancelled)
                .Select(b => new { b.amount, b.paid_amount })
                .ToListAsync();

            if (unpaidBills.Any())
            {
                var totalOutstanding = unpaidBills.Sum(b => b.amount - b.paid_amount);
                throw new ConflictException(
                    $"Cannot delete flat '{existing.FlatNo}' — it has {unpaidBills.Count} unpaid bill(s) totaling \u20b9{totalOutstanding:N2}. Settle all dues before deleting.");
            }

            await _repo.DeleteByPublicIdAsync(publicId, societyId);
            await _repo.SaveChangesAsync();
            _logger.LogInformation("Flat deleted successfully for PublicId {PublicId}", publicId);
            return true;
        }

       
        public async Task<IEnumerable<FlatStatusDto>> GetAllAsync()
        {
            var statuses = await _repo.GetAllAsync();
            return statuses.Select(s => new FlatStatusDto(s.Code, s.DisplayName));
        }

        /// <summary>
        /// Returns the ledger for a flat, including all adjustments and payments, with running balance.
        /// </summary>
        public async Task<FlatLedgerResponse> GetFlatLedgerAsync(Guid publicId, long userId, DateTime? startDate = null, DateTime? endDate = null)
        {
            var societyId = await _userContext.GetSocietyIdAsync(userId);

            // Get flat by publicId and verify it belongs to user's society (exclude soft-deleted)
            var flat = await _db.flats.FirstOrDefaultAsync(f => f.public_id == publicId && f.society_id == societyId && !f.is_deleted);
            if (flat == null)
                throw new NotFoundException("Flat", publicId.ToString());

            var flatId = flat.id;
            var ledgerEntries = new List<FlatLedgerEntryDto>();

            // Get all adjustments for the flat (maintenance charges, opening balance, etc.)
            var adjustmentsQuery = _db.adjustments
                .Where(a => a.flat_id == flatId && !a.is_deleted);

            if (startDate.HasValue)
                adjustmentsQuery = adjustmentsQuery.Where(a => a.created_at >= startDate.Value);
            // Use exclusive upper bound (< next day) so that records timestamped
            // anywhere on endDate's calendar day are included, not just at midnight.
            if (endDate.HasValue)
                adjustmentsQuery = adjustmentsQuery.Where(a => a.created_at < endDate.Value.Date.AddDays(1));

            var adjustments = await adjustmentsQuery
                .OrderBy(a => a.created_at)
                .ToListAsync();

            // Get all payments for the flat
            var paymentsQuery = _db.maintenance_payments
                .Where(p => p.flat_id == flatId && !p.is_deleted);

            if (startDate.HasValue)
                paymentsQuery = paymentsQuery.Where(p => p.payment_date >= startDate.Value);
            if (endDate.HasValue)
                paymentsQuery = paymentsQuery.Where(p => p.payment_date < endDate.Value.Date.AddDays(1));

            var payments = await paymentsQuery
                .OrderBy(p => p.payment_date)
                .ToListAsync();

            // Calculate opening balance (sum of adjustments before startDate minus payments before startDate)
            decimal openingBalance = 0;
            if (startDate.HasValue)
            {
                var adjustmentsBefore = await _db.adjustments
                    .Where(a => a.flat_id == flatId && a.created_at < startDate.Value && !a.is_deleted)
                    .SumAsync(a => (decimal?)a.amount) ?? 0;

                var paymentsBefore = await _db.maintenance_payments
                    .Where(p => p.flat_id == flatId && p.payment_date < startDate.Value && !p.is_deleted)
                    .SumAsync(p => (decimal?)p.amount) ?? 0;

                openingBalance = adjustmentsBefore - paymentsBefore;
            }

            // Add adjustment entries
            foreach (var adj in adjustments)
            {
                // Extract period from reason if it's monthly maintenance
                string? period = null;
                if (adj.entry_type == EntryTypeCodes.MonthlyMaintenance && adj.reason != null && adj.reason.Contains(" - "))
                {
                    var parts = adj.reason.Split(" - ");
                    if (parts.Length > 1)
                        period = parts[1];
                }

                ledgerEntries.Add(new FlatLedgerEntryDto
                {
                    Date = adj.created_at,
                    EntryType = "maintenance",
                    Period = period,
                    Charge = adj.amount,
                    Payment = 0,
                    Balance = 0, // Will be calculated below
                    Description = adj.reason,
                    ReferenceNumber = null
                });
            }

            // Add payment entries
            foreach (var pmt in payments)
            {
                ledgerEntries.Add(new FlatLedgerEntryDto
                {
                    Date = pmt.payment_date,
                    EntryType = "payment",
                    Period = null, // Payments are not linked to specific period
                    Charge = 0,
                    Payment = pmt.amount,
                    Balance = 0, // Will be calculated below
                    Description = $"Payment - {pmt.notes ?? ""}",
                    ReferenceNumber = pmt.reference_number
                });
            }

            // Sort all entries by date and calculate running balance
            ledgerEntries = ledgerEntries.OrderBy(e => e.Date).ToList();
            decimal runningBalance = openingBalance;

            foreach (var entry in ledgerEntries)
            {
                runningBalance += entry.Charge - entry.Payment;
                entry.Balance = runningBalance;
            }

            _logger.LogInformation("Flat ledger retrieved for flat {PublicId}", publicId);

            return new FlatLedgerResponse
            {
                FlatPublicId = flat.public_id,
                FlatNo = flat.flat_no,
                OwnerName = flat.owner_name,
                OpeningBalance = openingBalance,
                ClosingBalance = runningBalance,
                Entries = ledgerEntries
            };
        }

        /// <summary>
        /// Returns the financial summary for a flat, including opening balance, bill outstanding, total charges, and payments.
        /// </summary>
        public async Task<FlatFinancialSummaryResponse> GetFlatFinancialSummaryAsync(Guid publicId, long userId)
        {
            var societyId = await _userContext.GetSocietyIdAsync(userId);
            var summary = await GetFlatFinancialSummaryBySocietyAsync(publicId, societyId);
            if (summary == null)
                throw new NotFoundException("Flat", publicId.ToString());
            _logger.LogInformation("Financial summary retrieved for flat {PublicId}", publicId);
            return summary;
        }

        /// <summary>
        /// Internal helper — computes financial summary given a flat publicId and a pre-resolved societyId.
        /// Returns null if the flat does not exist or belongs to another society.
        /// </summary>
        private async Task<FlatFinancialSummaryResponse?> GetFlatFinancialSummaryBySocietyAsync(Guid publicId, long societyId)
        {
            var flat = await _db.flats
                .FirstOrDefaultAsync(f => f.public_id == publicId
                                       && f.society_id == societyId
                                       && !f.is_deleted);
            if (flat == null)
                return null;

            var openingBalanceRemaining = await _db.adjustments
                .Where(a => a.flat_id == flat.id && a.entry_type == EntryTypeCodes.OpeningBalance && !a.is_deleted)
                .SumAsync(a => (decimal?)a.remaining_amount) ?? 0;

            // Exclude cancelled bills — a cancelled bill is not a real outstanding obligation.
            var billOutstanding = await _db.bills
                .Where(b => b.flat_id == flat.id && !b.is_deleted
                         && b.status_code != BillStatusCodes.Cancelled)
                .SumAsync(b => (decimal?)(b.amount - b.paid_amount)) ?? 0;

            var totalCharges = await _db.bills
                .Where(b => b.flat_id == flat.id && !b.is_deleted)
                .SumAsync(b => (decimal?)b.amount) ?? 0;

            var totalPayments = await _db.maintenance_payments
                .Where(p => p.flat_id == flat.id && !p.is_deleted)
                .SumAsync(p => (decimal?)p.amount) ?? 0;

            return new FlatFinancialSummaryResponse
            {
                OpeningBalanceRemaining = openingBalanceRemaining,
                BillOutstanding = billOutstanding,
                TotalOutstanding = openingBalanceRemaining + billOutstanding,
                TotalCharges = totalCharges,
                TotalPayments = totalPayments
            };
        }

        /// <summary>
        /// Returns financial summaries for multiple flats in a single call.
        /// Silently skips IDs that don't exist or belong to another society. Capped at 500 IDs.
        /// </summary>
        public async Task<BulkFinancialSummaryResponse> GetBulkFinancialSummaryAsync(IEnumerable<Guid> flatPublicIds, long userId)
        {
            var societyId = await _userContext.GetSocietyIdAsync(userId);
            var ids = flatPublicIds.Distinct().Take(500).ToList();

            // Resolve flat internal IDs in one query, scoped to the society
            var flatMap = await _db.flats
                .Where(f => ids.Contains(f.public_id) && f.society_id == societyId && !f.is_deleted)
                .Select(f => new { f.id, f.public_id })
                .ToListAsync();

            var flatIds = flatMap.Select(f => f.id).ToList();

            // Batch-fetch all required data in 3 queries instead of N*3
            var openingBalances = await _db.adjustments
                .Where(a => a.flat_id.HasValue && flatIds.Contains(a.flat_id.Value) && a.entry_type == EntryTypeCodes.OpeningBalance && !a.is_deleted)
                .GroupBy(a => a.flat_id!.Value)
                .Select(g => new { FlatId = g.Key, Remaining = g.Sum(a => (decimal?)a.remaining_amount) ?? 0 })
                .ToListAsync();

            // Exclude cancelled bills from outstanding — same rule as single-flat summary.
            var billData = await _db.bills
                .Where(b => flatIds.Contains(b.flat_id) && !b.is_deleted
                         && b.status_code != BillStatusCodes.Cancelled)
                .GroupBy(b => b.flat_id)
                .Select(g => new
                {
                    FlatId = g.Key,
                    Outstanding = g.Sum(b => (decimal?)(b.amount - b.paid_amount)) ?? 0,
                    TotalCharges = g.Sum(b => (decimal?)b.amount) ?? 0
                })
                .ToListAsync();

            var paymentData = await _db.maintenance_payments
                .Where(p => flatIds.Contains(p.flat_id) && !p.is_deleted)
                .GroupBy(p => p.flat_id)
                .Select(g => new { FlatId = g.Key, TotalPayments = g.Sum(p => (decimal?)p.amount) ?? 0 })
                .ToListAsync();

            var obLookup = openingBalances.ToDictionary(x => x.FlatId, x => x.Remaining);
            var billLookup = billData.ToDictionary(x => x.FlatId, x => x);
            var payLookup = paymentData.ToDictionary(x => x.FlatId, x => x.TotalPayments);

            var result = new BulkFinancialSummaryResponse();
            foreach (var flat in flatMap)
            {
                var ob = obLookup.GetValueOrDefault(flat.id, 0);
                var billOutstanding = billLookup.TryGetValue(flat.id, out var bd) ? bd.Outstanding : 0;
                var totalCharges = billLookup.TryGetValue(flat.id, out var bd2) ? bd2.TotalCharges : 0;
                var totalPayments = payLookup.GetValueOrDefault(flat.id, 0);

                result.Summaries[flat.public_id.ToString()] = new FlatFinancialSummaryResponse
                {
                    OpeningBalanceRemaining = ob,
                    BillOutstanding = billOutstanding,
                    TotalOutstanding = ob + billOutstanding,
                    TotalCharges = totalCharges,
                    TotalPayments = totalPayments
                };
            }

            _logger.LogInformation("Bulk financial summary computed for {Count} flats in society {SocietyId}", result.Summaries.Count, societyId);
            return result;
        }

        private static readonly HashSet<string> AllowedFlatSortFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "flatNo", "ownerName", "maintenanceAmount", "createdAt"
        };

        /// <summary>
        /// Returns a paginated, filtered, and sorted list of flats for the user's society.
        /// </summary>
        public async Task<PagedFlatsResponse> GetPagedAsync(
            long userId,
            string? search,
            string? statusCode,
            int page,
            int size,
            string sortBy,
            string sortDir)
        {
            var societyId = await _userContext.GetSocietyIdAsync(userId);

            var query = _db.flats
                .Include(f => f.status)
                .Include(f => f.society)
                .Where(f => f.society_id == societyId && !f.is_deleted)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.ToLower();
                query = query.Where(f =>
                    (f.flat_no != null && f.flat_no.ToLower().Contains(term)) ||
                    (f.owner_name != null && f.owner_name.ToLower().Contains(term)) ||
                    (f.contact_mobile != null && f.contact_mobile.ToLower().Contains(term)) ||
                    (f.contact_email != null && f.contact_email.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(statusCode))
                query = query.Where(f => f.status != null && f.status.code == statusCode);

            query = (sortBy.ToLower(), sortDir.ToLower()) switch
            {
                ("flatno", "desc")            => query.OrderByDescending(f => f.flat_no),
                ("flatno", _)                 => query.OrderBy(f => f.flat_no),
                ("ownername", "desc")         => query.OrderByDescending(f => f.owner_name),
                ("ownername", _)              => query.OrderBy(f => f.owner_name),
                ("maintenanceamount", "desc") => query.OrderByDescending(f => f.maintenance_amount),
                ("maintenanceamount", _)      => query.OrderBy(f => f.maintenance_amount),
                ("createdat", "desc")         => query.OrderByDescending(f => f.created_at),
                _                             => query.OrderBy(f => f.created_at),
            };

            var totalCount = await query.LongCountAsync();
            var totalPages = size > 0 ? (int)Math.Ceiling((double)totalCount / size) : 0;

            var items = await query
                .Skip(page * size)
                .Take(size)
                .ToListAsync();

            var itemIds = items.Select(f => f.id).ToList();
            var outstanding = await ComputeOutstandingByFlatIdAsync(itemIds);

            return new PagedFlatsResponse
            {
                Content = items.Select(f => MapEfToDto(f, outstanding.GetValueOrDefault(f.id, 0m))).ToList(),
                TotalElements = totalCount,
                TotalPages = totalPages,
                Page = page,
                Size = size
            };
        }


        #region Mapping helpers

        private static FlatResponseDto MapToDto(Flat f, decimal totalOutstanding = 0m)
        {
            return new FlatResponseDto(
                f.PublicId,
                f.SocietyPublicId,
                f.FlatNo,
                f.OwnerName,
                f.ContactMobile,
                f.ContactEmail,
                f.MaintenanceAmount,
                f.StatusId,
                f.StatusName,
                f.CreatedAt,
                f.UpdatedAt
            ) { TotalOutstanding = totalOutstanding };
        }

        /// <summary>
        /// Maps directly from the EF entity (used in paged queries that hit _db.flats directly).
        /// </summary>
        private static FlatResponseDto MapEfToDto(SocietyLedger.Infrastructure.Persistence.Entities.flat f, decimal totalOutstanding = 0m)
        {
            return new FlatResponseDto(
                f.public_id,
                f.society?.public_id ?? Guid.Empty,
                f.flat_no,
                f.owner_name,
                f.contact_mobile,
                f.contact_email,
                f.maintenance_amount,
                f.status_id,
                f.status?.display_name ?? string.Empty,
                f.created_at,
                f.updated_at
            ) { TotalOutstanding = totalOutstanding };
        }

        private async Task<Dictionary<long, decimal>> ComputeOutstandingByFlatIdAsync(List<long> flatIds)
        {
            if (flatIds.Count == 0) return new Dictionary<long, decimal>();

            var obRemaining = await _db.adjustments
                .Where(a => a.flat_id.HasValue && flatIds.Contains(a.flat_id.Value)
                         && a.entry_type == EntryTypeCodes.OpeningBalance && !a.is_deleted)
                .GroupBy(a => a.flat_id!.Value)
                .Select(g => new { FlatId = g.Key, Remaining = g.Sum(a => (decimal?)a.remaining_amount) ?? 0 })
                .ToDictionaryAsync(x => x.FlatId, x => x.Remaining);

            var billOutstanding = await _db.bills
                .Where(b => flatIds.Contains(b.flat_id) && !b.is_deleted
                         && b.status_code != BillStatusCodes.Cancelled)
                .GroupBy(b => b.flat_id)
                .Select(g => new { FlatId = g.Key, Outstanding = g.Sum(b => (decimal?)(b.amount - b.paid_amount)) ?? 0 })
                .ToDictionaryAsync(x => x.FlatId, x => x.Outstanding);

            return flatIds.ToDictionary(
                id => id,
                id => obRemaining.GetValueOrDefault(id, 0m) + billOutstanding.GetValueOrDefault(id, 0m));
        }

        #endregion
    }
}
