using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SocietyLedger.Application.DTOs.OpeningBalance;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Persistence.Entities;

namespace SocietyLedger.Infrastructure.Services
{
    public class OpeningBalanceService : IOpeningBalanceService
    {
        private readonly AppDbContext _db;
        private readonly ISocietyRepository _societyRepo;
        private readonly IUserRepository _userRepo;
        private readonly ILogger<OpeningBalanceService> _logger;

        public OpeningBalanceService(
            AppDbContext db,
            ISocietyRepository societyRepo,
            IUserRepository userRepo,
            ILogger<OpeningBalanceService> logger)
        {
            _db          = db;
            _societyRepo = societyRepo;
            _userRepo    = userRepo;
            _logger      = logger;
        }

        /// <summary>
        /// Returns opening balance summary for a society, including member dues and advances.
        /// </summary>
        public async Task<OpeningBalanceSummaryResponse?> GetSummaryAsync(long societyId)
        {
            // Check if opening balance is applied
            var applied = await _db.adjustments.AnyAsync(a => a.society_id == societyId && a.entry_type == EntryTypeCodes.OpeningBalance && !a.is_deleted)
                || await _db.society_fund_ledgers.AnyAsync(f => f.society_id == societyId && f.entry_type == EntryTypeCodes.OpeningFund && !f.is_deleted!.Value);
            if (!applied)
                return null;

            // Society opening amount (initial fund/bank balance)
            var societyOpeningAmount = await _db.society_fund_ledgers
                .Where(f => f.society_id == societyId && f.entry_type == EntryTypeCodes.OpeningFund && !f.is_deleted!.Value)
                .SumAsync(f => (decimal?)f.amount) ?? 0;

            // Member dues: sum of all member outstanding dues (remaining_amount > 0)
            var totalMemberDues = await _db.adjustments
                .Where(a => a.society_id == societyId && a.entry_type == EntryTypeCodes.OpeningBalance && !a.is_deleted && a.remaining_amount > 0)
                .SumAsync(a => (decimal?)a.remaining_amount) ?? 0;

            // Member advances: sum of all member advances/prepaid amounts (remaining_amount < 0)
            var totalMemberAdvance = await _db.adjustments
                .Where(a => a.society_id == societyId && a.entry_type == EntryTypeCodes.OpeningBalance && !a.is_deleted && a.remaining_amount < 0)
                .SumAsync(a => (decimal?)-a.remaining_amount) ?? 0;

            return new OpeningBalanceSummaryResponse
            {
                SocietyOpeningAmount = societyOpeningAmount,
                TotalMemberDues = totalMemberDues,
                TotalMemberAdvance = totalMemberAdvance
            };
        }

        // Carries the minimum status metadata needed from each opening-balance source.
        private sealed record OpeningMeta(DateOnly TransactionDate, DateTime AuditCreatedAt, long CreatedBy);

        /// <summary>
        /// Returns opening balance status for a society, including transaction date and applied by.
        /// </summary>
        public async Task<OpeningBalanceStatusResponse> GetStatusAsync(long societyId)
        {
            // member-side opening (adjustments table)
            var memberOpening = await _db.adjustments
                .Where(a => a.society_id == societyId
                         && a.entry_type == EntryTypeCodes.OpeningBalance
                         && !a.is_deleted)
                // Order by the business date so that the "earliest" financial event
                // is selected, not the earliest row insertion.
                .OrderBy(a => a.created_at)   // adjustments table has no transaction_date yet
                .Select(a => new OpeningMeta(
                    DateOnly.FromDateTime(a.created_at),   // fallback: derive from created_at
                    a.created_at,
                    a.created_by ?? 0
                ))
                .FirstOrDefaultAsync();

            // society-side opening (fund ledger)
            var societyOpening = await _db.society_fund_ledgers
                .Where(f => f.society_id == societyId
                         && f.entry_type == EntryTypeCodes.OpeningFund
                         && !f.is_deleted!.Value)
                // Use transaction_date for financial ordering — NOT created_at.
                .OrderBy(f => f.transaction_date)
                .Select(f => new OpeningMeta(
                    f.transaction_date ?? DateOnly.FromDateTime(f.created_at),
                    f.created_at,
                    f.created_by
                ))
                .FirstOrDefaultAsync();

            var applied = memberOpening != null || societyOpening != null;

            if (!applied)
            {
                return new OpeningBalanceStatusResponse
                {
                    IsApplied = false,
                    TransactionDate = null,
                    AuditCreatedAt = null,
                    AppliedBy = null
                };
            }

            // Prefer the earliest financial date across both sources.
            var source = (memberOpening, societyOpening) switch
            {
                ({ } m, null)   => m,
                (null, { } s)   => s,
                ({ } m, { } s)  => m.TransactionDate <= s.TransactionDate ? m : s,
                _               => throw new InvalidOperationException("Unreachable")
            };

            var userName = await _userRepo.GetByIdAsync(source.CreatedBy)
                .ContinueWith(t => t.Result?.Name);

            return new OpeningBalanceStatusResponse
            {
                IsApplied = true,
                TransactionDate = source.TransactionDate,
                AuditCreatedAt = source.AuditCreatedAt,
                AppliedBy = userName
            };
        }

        /// <summary>
        /// Applies opening balance for a society, validates transaction date and prevents duplicates.
        /// </summary>
        public async Task ApplyOpeningBalanceAsync(OpeningBalanceRequest request, long societyId, long userId)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            // ── 1. Load the society to resolve onboarding_date for date validation ─
            var onboardingDate = await _societyRepo.GetOnboardingDateAsync(societyId)
                ?? throw new NotFoundException("Society", societyId.ToString());

            // ── 2. Validate transaction_date against financial epoch ──────────────
            if (request.TransactionDate < onboardingDate)
                throw new ValidationException(
                    $"transaction_date ({request.TransactionDate:yyyy-MM-dd}) cannot be earlier than " +
                    $"the society onboarding date ({onboardingDate:yyyy-MM-dd}).");

            if (request.TransactionDate > DateOnly.FromDateTime(DateTime.UtcNow))
                throw new ValidationException(
                    $"transaction_date ({request.TransactionDate:yyyy-MM-dd}) cannot be in the future.");

            // ── 3. Duplicate guard (application-level for friendly error messages) ─
            //      The DB also enforces this via the uq_society_single_opening partial
            //      unique index as a safety net against concurrent inserts.
            var memberOpeningExists = await _db.adjustments
                .AnyAsync(a => a.society_id == societyId
                            && a.entry_type == EntryTypeCodes.OpeningBalance
                            && !a.is_deleted);

            var societyOpeningExists = await _db.society_fund_ledgers
                .AnyAsync(f => f.society_id == societyId
                            && f.entry_type == EntryTypeCodes.OpeningFund
                            && !f.is_deleted!.Value);

            if (memberOpeningExists || societyOpeningExists)
                throw new ValidationException(
                    "Opening balance has already been applied for this society. " +
                    "It can only be set once.");

            // ── 4. Require at least one non-zero value ────────────────────────────
            var hasItems = request.Items?.Any(i => i.Amount != 0) == true;
            var hasSocietyAmount = request.SocietyOpeningAmount > 0;

            if (!hasItems && !hasSocietyAmount)
                throw new ValidationException(
                    "At least one opening value must be provided (society amount or a flat amount).");

            using var transaction = await _db.Database.BeginTransactionAsync();

            try
            {
                var now = DateTime.UtcNow;

                // ── 5a. Member opening dues (adjustments table) ───────────────────
                if (hasItems)
                {
                    var flatPublicIds = request.Items!
                        .Where(i => i.Amount != 0)
                        .Select(i => i.FlatPublicId)
                        .ToList();

                    var flats = await _db.flats
                        .Where(f => flatPublicIds.Contains(f.public_id)
                                 && f.society_id == societyId)
                        .ToDictionaryAsync(f => f.public_id);

                    foreach (var item in request.Items!.Where(i => i.Amount != 0))
                    {
                        if (!flats.TryGetValue(item.FlatPublicId, out var flat))
                            throw new ValidationException(
                                $"Flat {item.FlatPublicId} not found or does not belong to this society.");

                        _db.adjustments.Add(new adjustment
                        {
                            society_id        = societyId,
                            flat_id           = flat.id,
                            amount            = item.Amount,
                            remaining_amount  = item.Amount,
                            reason            = "Opening Balance - Migration",
                            created_by        = userId,
                            entry_type        = EntryTypeCodes.OpeningBalance,
                            created_at        = now,   // audit only
                            is_deleted        = false
                        });
                    }
                }

                // ── 5b. Society opening fund (bank balance — fund ledger) ─────────
                if (hasSocietyAmount)
                {
                    _db.society_fund_ledgers.Add(BuildLedgerEntry(
                        societyId:       societyId,
                        amount:          request.SocietyOpeningAmount,
                        entryType:       EntryTypeCodes.OpeningFund,
                        transactionDate: request.TransactionDate,
                        notes:           "Opening bank balance during migration",
                        reference:       null,
                        userId:          userId,
                        auditNow:        now));
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation(
                    "Opening balance applied for society {SocietyId}. " +
                    "TransactionDate: {TxDate}, Items: {ItemCount}, SocietyFund: {Amount}",
                    societyId,
                    request.TransactionDate,
                    request.Items?.Count(i => i.Amount != 0) ?? 0,
                    request.SocietyOpeningAmount);
            }
            catch
            {
                await transaction.RollbackAsync();
                _logger.LogError("Error applying opening balance for society {SocietyId}", societyId);
                throw;
            }
        }

        // ── Private factory ───────────────────────────────────────────────────────
        // Centralises society_fund_ledger creation so that every callsite is forced
        // to supply transaction_date explicitly.  created_at is set from auditNow and
        // is NEVER used as the financial date.
        private static society_fund_ledger BuildLedgerEntry(
            long     societyId,
            decimal  amount,
            string   entryType,
            DateOnly transactionDate,
            string?  notes,
            string?  reference,
            long     userId,
            DateTime auditNow) => new()
        {
            public_id        = Guid.NewGuid(),
            society_id       = societyId,
            amount           = amount,
            entry_type       = entryType,
            transaction_date = transactionDate,   // ← financial event date
            created_at       = auditNow,          // ← audit timestamp only
            reference        = reference,
            notes            = notes,
            created_by       = userId,
            is_deleted       = false
        };
    }
}

