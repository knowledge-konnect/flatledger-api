using ClosedXML.Excel;
using FluentAssertions;
using SocietyLedger.Application.DTOs.Reports;
using SocietyLedger.Infrastructure.Services;

namespace SocietyLedger.Tests.Services;

public class ReportExportServiceTests
{
    [Fact]
    public void GenerateMonthlyReport_DoesNotIncludeBalanceSignLegendNote()
    {
        var service = new ReportExportService();
        var data = new MonthlyReportDto
        {
            SocietyName = "Ramana Royale",
            PeriodLabel = "August 2026",
            PaymentSummary = new PaymentSummaryDto
            {
                TotalFlats = 1,
                Paid = 1,
                Pending = 0,
                TotalBilled = 1000m,
                TotalCollected = 1000m,
                PendingAmount = 0m,
                OtherIncome = 0m,
                CollectionEfficiency = 100m
            },
            FlatDetails =
            [
                new FlatDetailDto
                {
                    FlatNo = "101",
                    OwnerName = "Sarat",
                    OpeningBalance = 0m,
                    CurrentBill = 1000m,
                    CurrentPaid = 1000m,
                    TotalDue = 1000m,
                    BalanceAmount = 0m,
                    Status = "paid"
                }
            ]
        };

        var bytes = service.GenerateMonthlyReport(data);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var paymentSheet = workbook.Worksheet("Payments");
        var cells = paymentSheet.CellsUsed().Select(c => c.GetString()).ToList();

        cells.Should().NotContain(x => x.Contains("Positive = member owes the society", StringComparison.OrdinalIgnoreCase));
        cells.Should().NotContain(x => x.Contains("Negative = society owes member", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GenerateMonthlyReport_IncludesFlatDetailsInOtherIncomeSection()
    {
        var service = new ReportExportService();
        var data = new MonthlyReportDto
        {
            SocietyName = "Ramana Royale",
            PeriodLabel = "August 2026",
            PaymentSummary = new PaymentSummaryDto
            {
                TotalFlats = 1,
                Paid = 1,
                Pending = 1,
                TotalBilled = 1000m,
                TotalCollected = 200m,
                PendingAmount = 1000m,
                OtherIncome = 200m,
                CollectionEfficiency = 20m
            },
            FlatDetails =
            [
                new FlatDetailDto
                {
                    FlatNo = "401",
                    OwnerName = "Rajesh",
                    OpeningBalance = 0m,
                    CurrentBill = 1000m,
                    CurrentPaid = 0m,
                    TotalDue = 1000m,
                    BalanceAmount = 1000m,
                    Status = "unpaid"
                }
            ],
            IncomeDetails =
            [
                new IncomeDetailDto
                {
                    FlatNo = "401",
                    OwnerName = "Rajesh",
                    DatePaid = new DateOnly(2026, 8, 12),
                    CategoryName = "Parking Income",
                    Description = "Visitor parking",
                    Amount = 200m
                }
            ]
        };

        var bytes = service.GenerateMonthlyReport(data);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var paymentsSheet = workbook.Worksheet("Payments");
        var cells = paymentsSheet.CellsUsed().Select(c => c.GetString()).ToList();

        cells.Should().Contain("Flat No");
        cells.Should().Contain("Owner / Tenant");
        cells.Should().Contain("401");
        cells.Should().Contain("Rajesh");
        cells.Should().Contain("Parking Income");
    }

    [Fact]
    public void GenerateMonthlyReport_RemovesFinancialNotesAndAddsBuildingIconToTitle()
    {
        var service = new ReportExportService();
        var data = new MonthlyReportDto
        {
            SocietyName = "Ramana Royale",
            PeriodLabel = "August 2026",
            PaymentSummary = new PaymentSummaryDto
            {
                TotalFlats = 1,
                Paid = 1,
                Pending = 0,
                TotalBilled = 1000m,
                TotalCollected = 1000m,
                PendingAmount = 0m,
                OtherIncome = 0m,
                CollectionEfficiency = 100m
            },
            FlatDetails =
            [
                new FlatDetailDto
                {
                    FlatNo = "101",
                    OwnerName = "Sarat",
                    OpeningBalance = 0m,
                    CurrentBill = 1000m,
                    CurrentPaid = 1000m,
                    TotalDue = 1000m,
                    BalanceAmount = 0m,
                    Status = "paid"
                }
            ]
        };

        var bytes = service.GenerateMonthlyReport(data);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var overviewSheet = workbook.Worksheet("Overview");
        var cells = overviewSheet.CellsUsed().Select(c => c.GetString()).ToList();

        cells.Should().NotContain(x => x.Contains("Financial Notes", StringComparison.OrdinalIgnoreCase));
        cells.Should().NotContain(x => x.Contains("Filters", StringComparison.OrdinalIgnoreCase));
        cells.Should().Contain(x => x.Contains("🏢", StringComparison.OrdinalIgnoreCase));
    }
}
