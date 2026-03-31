using SocietyLedger.Application.DTOs.Reports;

namespace SocietyLedger.Application.Interfaces.Services
{
    public interface IReportExportService
    {
        byte[] GenerateMonthlyReport(MonthlyReportDto data);
        byte[] GenerateYearlyReport(YearlyReportDto data);
    }
}
