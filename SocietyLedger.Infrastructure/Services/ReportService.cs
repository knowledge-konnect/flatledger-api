using SocietyLedger.Application.DTOs.Reports;

using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Infrastructure.Services.Common;
using SocietyLedger.Shared;

namespace SocietyLedger.Infrastructure.Services
{
    public class ReportService : IReportService
    {
        private readonly IReportRepository _reportRepo;
        private readonly IUserContext _userContext;
        private readonly IReportExportService _exportService;

        public ReportService(IReportRepository reportRepo, IUserContext userContext, IReportExportService exportService)
        {
            _reportRepo = reportRepo;
            _userContext = userContext;
            _exportService = exportService;
        }

        public async Task<CollectionSummaryDto> GetCollectionSummaryAsync(
            long userId, string? startPeriod, string? endPeriod, CancellationToken ct = default)
        {
            var societyId = await _userContext.GetSocietyIdAsync(userId);
            return await _reportRepo.GetCollectionSummaryAsync(societyId, startPeriod, endPeriod, ct);
        }

        public async Task<List<DefaulterDto>> GetDefaultersReportAsync(
            long userId, decimal minOutstanding = 0, CancellationToken ct = default)
        {
            var societyId = await _userContext.GetSocietyIdAsync(userId);
            return await _reportRepo.GetDefaultersReportAsync(societyId, minOutstanding, ct);
        }

        public async Task<IncomeVsExpenseDto> GetIncomeVsExpenseAsync(
            long userId, DateOnly? startDate, DateOnly? endDate, CancellationToken ct = default)
        {
            var societyId = await _userContext.GetSocietyIdAsync(userId);
            return await _reportRepo.GetIncomeVsExpenseAsync(societyId, startDate, endDate, ct);
        }

        public async Task<FundLedgerReportDto> GetFundLedgerAsync(
            long userId, DateOnly? startDate, DateOnly? endDate, CancellationToken ct = default)
        {
            var societyId = await _userContext.GetSocietyIdAsync(userId);
            var result = await _reportRepo.GetFundLedgerAsync(societyId, startDate, endDate, ct);

            // Ensure opening_fund entries always lead regardless of their recorded date,
            // then recalculate running balance so it never goes negative at the start.
            result.Entries = result.Entries
                .OrderBy(e => e.EntryType == "opening_fund" ? 0 : 1)
                .ThenBy(e => e.Date)
                .ToList();

            decimal running = result.OpeningBalance;
            foreach (var entry in result.Entries)
            {
                running += entry.Credit - entry.Debit;
                entry.RunningBalance = running;
            }

            return result;
        }

        public async Task<PagedResult<PaymentRegisterDto>> GetPaymentRegisterAsync(
            long userId, DateOnly? startDate, DateOnly? endDate, int page, int pageSize, CancellationToken ct = default)
        {
            var societyId = await _userContext.GetSocietyIdAsync(userId);
            return await _reportRepo.GetPaymentRegisterAsync(societyId, startDate, endDate, page, pageSize, ct);
        }

        public async Task<ExpenseByCategoryDto> GetExpenseByCategoryAsync(
            long userId, DateOnly? startDate, DateOnly? endDate, CancellationToken ct = default)
        {
            var societyId = await _userContext.GetSocietyIdAsync(userId);
            return await _reportRepo.GetExpenseByCategoryAsync(societyId, startDate, endDate, ct);
        }

        public async Task<(byte[] Bytes, string FileName)> DownloadMonthlyReportAsync(
            long userId, int year, int month, CancellationToken ct = default)
        {
            var societyId = await _userContext.GetSocietyIdAsync(userId);
            var data = await _reportRepo.GetMonthlyReportDataAsync(societyId, year, month, ct);
            var bytes = _exportService.GenerateMonthlyReport(data);
            var monthName = new DateTime(year, month, 1).ToString("MMMM");
            var safeName = SanitizeFileName(data.SocietyName);
            return (bytes, $"FlatLedger_Monthly_Report_{safeName}_{monthName}_{year}.xlsx");
        }

        public async Task<(byte[] Bytes, string FileName)> DownloadYearlyReportAsync(
            long userId, int year, string yearType, CancellationToken ct = default)
        {
            var societyId = await _userContext.GetSocietyIdAsync(userId);
            var data = await _reportRepo.GetYearlyReportDataAsync(societyId, year, yearType, ct);
            var bytes = _exportService.GenerateYearlyReport(data);
            var label = yearType == "financial"
                ? $"FY_{year - 1}-{year % 100:D2}"
                : year.ToString();
            var safeName = SanitizeFileName(data.SocietyName);
            return (bytes, $"FlatLedger_Yearly_Report_{safeName}_{label}.xlsx");
        }

        private static string SanitizeFileName(string name)
        {
            var safe = new System.Text.StringBuilder();
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '-') safe.Append(c);
                else if (c == ' ') safe.Append('_');
            }
            return safe.Length > 0 ? safe.ToString() : "Society";
        }
    }
}