using ClosedXML.Excel;
using SocietyLedger.Application.DTOs.Reports;
using SocietyLedger.Application.Interfaces.Services;

namespace SocietyLedger.Infrastructure.Services
{
    public class ReportExportService : IReportExportService
    {
        // ── Design tokens ─────────────────────────────────────────────────────
        private const string FontName       = "Calibri";
        private const int    TitleFontSize  = 22;
        private const int    SubtitleSize   = 14;
        private const int    SectionSize    = 12;
        private const int    DataSize       = 11;
        private const string AmountFormat   = "₹#,##0";

        // Layout: A–B are left-margin columns; all content starts at column C (index 3).
        // Right margin begins at column I (index 9) and beyond.
        private const int ColStart = 3; // Column C — first content column

        // Emerald palette - Enhanced for professional appearance
        private static readonly XLColor ColourHeaderBg      = XLColor.FromHtml("#064E3B"); // dark emerald - main headers
        private static readonly XLColor ColourSectionBg     = XLColor.FromHtml("#065F46"); // section headers
        private static readonly XLColor ColourLightBg       = XLColor.FromHtml("#D1FAE5"); // light green backgrounds
        private static readonly XLColor ColourHighlightBg   = XLColor.FromHtml("#A7F3D0"); // important highlights
        private static readonly XLColor ColourAlternateRow  = XLColor.FromHtml("#F0FDF4"); // alternating table rows
        private static readonly XLColor ColourBorder        = XLColor.FromHtml("#D1D5DB"); // borders
        private static readonly XLColor ColourBorderDark    = XLColor.FromHtml("#9CA3AF"); // stronger borders
        private static readonly XLColor ColourPaidText      = XLColor.FromHtml("#059669"); // paid / positive
        private static readonly XLColor ColourPaidBg        = XLColor.FromHtml("#ECFDF5"); // paid status background
        private static readonly XLColor ColourPendingText   = XLColor.FromHtml("#DC2626"); // pending / negative
        private static readonly XLColor ColourPendingBg     = XLColor.FromHtml("#FEF2F2"); // pending status background
        private static readonly XLColor ColourTotalRowBg    = XLColor.FromHtml("#A7F3D0"); // total row - darker for emphasis
        private static readonly XLColor ColourCardBg        = XLColor.FromHtml("#ECFDF5"); // card-style sections
        private static readonly XLColor ColourWhite         = XLColor.White;

        // ── Monthly report (3 sheets) ────────────────────────────────────────
        public byte[] GenerateMonthlyReport(MonthlyReportDto data)
        {
            using var workbook = new XLWorkbook();
            BuildMonthlyOverviewSheet(workbook, data);
            BuildMonthlyPaymentsSheet(workbook, data);
            BuildMonthlyExpensesSheet(workbook, data);
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        // ── Yearly report (3 sheets) ─────────────────────────────────────────
        public byte[] GenerateYearlyReport(YearlyReportDto data)
        {
            using var workbook = new XLWorkbook();
            BuildYearlyOverviewSheet(workbook, data);
            BuildYearlyMonthlySummarySheet(workbook, data);
            BuildYearlyExpensesSheet(workbook, data);
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Monthly sheet builders
        // ─────────────────────────────────────────────────────────────────────

        private static void BuildMonthlyOverviewSheet(XLWorkbook wb, MonthlyReportDto data)
        {
            // Content occupies C(3)–G(7): label col C, value col D, headers/alerts span C:G
            const int colEnd = ColStart + 4; // column G
            var ws = wb.AddWorksheet("Overview");
            SetSheetDefaults(ws);
            int row = 1;

            row = WriteReportTitle(ws, row, ColStart, colEnd, data.SocietyName, $"Monthly Report - {data.PeriodLabel}");
            ws.SheetView.FreezeRows(2); // freeze branded title rows 1-2

            // Enhanced legend note
            ws.Range(row, ColStart, row, colEnd).Merge();
            ws.Cell(row, ColStart).Value = "Note: Positive = member owes the society; Negative = society owes member (advance).";
            ws.Cell(row, ColStart).Style.Font.Italic = true;
            ws.Cell(row, ColStart).Style.Font.FontSize = 9;
            ws.Cell(row, ColStart).Style.Font.FontColor = XLColor.DarkGray;
            ws.Cell(row, ColStart).Style.Fill.BackgroundColor = XLColor.FromHtml("#F9FAFB");
            ws.Cell(row, ColStart).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            ws.Cell(row, ColStart).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Cell(row, ColStart).Style.Border.OutsideBorderColor = ColourBorder;
            ws.Row(row).Height = 20;
            row += 2;

            // Fund Position
            row = WriteSectionHeader(ws, row, ColStart, colEnd, "Fund Position");
            row = WriteKvRow(ws, row, ColStart, "Opening Balance", data.FundPosition.OpeningBalance, isAmount: true);
            row = WriteKvRow(ws, row, ColStart, "Collected",       data.FundPosition.Collected,       isAmount: true);
            row = WriteKvRow(ws, row, ColStart, "Expenses",        data.FundPosition.Expenses,        isAmount: true);
            row = WriteKvRow(ws, row, ColStart, "Closing Balance", data.FundPosition.ClosingBalance,  isAmount: true, bold: true, highlight: true);
            row++;

            // Payment Summary
            var ps = data.PaymentSummary ?? new PaymentSummaryDto();
            row = WriteSectionHeader(ws, row, ColStart, colEnd, "Payment Summary");
            row = WriteKvRow(ws, row, ColStart, "Total Flats", ps.TotalFlats);
            row = WriteKvRow(ws, row, ColStart, "Paid",        ps.Paid,    valueColor: ColourPaidText);
            row = WriteKvRow(ws, row, ColStart, "Pending",           ps.Pending,                 valueColor: ps.Pending > 0 ? ColourPendingText : null);
            row = WriteKvRow(ws, row, ColStart, "Total Billed",       ps.TotalBilled,             isAmount: true);
            row = WriteKvRow(ws, row, ColStart, "Total Collected",    ps.TotalCollected,          isAmount: true, valueColor: ColourPaidText);
            row = WriteKvRow(ws, row, ColStart, "Pending Amount",     ps.PendingAmount,           isAmount: true, valueColor: ps.PendingAmount > 0 ? ColourPendingText : null);
            row = WriteKvRow(ws, row, ColStart, "Collection Efficiency", $"{ps.CollectionEfficiency}%");
            row++;

            // Alerts
            row = WriteSectionHeader(ws, row, ColStart, colEnd, "Alerts");
            if (ps.Pending > 0 && data.Alerts?.Count > 0)
            {
                foreach (var alert in data.Alerts)
                    row = WriteAlertRow(ws, row, ColStart, colEnd, alert, isWarning: true);
            }
            else
            {
                row = WriteAlertRow(ws, row, ColStart, colEnd, "All payments completed", isWarning: false);
            }
            row++;

            // Summary
            if (!string.IsNullOrWhiteSpace(data.Summary))
            {
                row = WriteSectionHeader(ws, row, ColStart, colEnd, "Summary");
                ws.Range(row, ColStart, row, colEnd).Merge();
                ws.Cell(row, ColStart).Value = data.Summary;
                ws.Cell(row, ColStart).Style.Fill.BackgroundColor = XLColor.FromHtml("#F0FDF4");
                ws.Cell(row, ColStart).Style.Alignment.WrapText = true;
                ws.Cell(row, ColStart).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                ws.Cell(row, ColStart).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                ws.Cell(row, ColStart).Style.Font.FontSize = DataSize;
                ws.Cell(row, ColStart).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                ws.Cell(row, ColStart).Style.Border.OutsideBorderColor = ColourBorder;
                ws.Row(row).Height = 40;
                row += 2;
            }

            FinalizeSheet(ws, ColStart, colEnd);
        }

        private static void BuildMonthlyPaymentsSheet(XLWorkbook wb, MonthlyReportDto data)
        {
            // C(3)=Flat No D(4)=Owner E(5)=Previous Balance F(6)=Monthly Charges G(7)=Total Due (Before Payment) H(8)=Amount Paid I(9)=Outstanding J(10)=Status
            const int colEnd = ColStart + 7; // column J
            var ws = wb.AddWorksheet("Payments");
            SetSheetDefaults(ws);
            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape; // Landscape for wider table
            int row = 1;

            row = WriteReportTitle(ws, row, ColStart, colEnd, data.SocietyName, $"Payments - {data.PeriodLabel}");

            // Enhanced note styling
            int noteRow = row;
            ws.Range(noteRow, ColStart, noteRow, colEnd).Merge();
            ws.Cell(noteRow, ColStart).Value = "Note: 'Total Due (Before Payment)' = Previous Balance + Monthly Charges. 'Outstanding' = Total Due - Amount Paid.";
            ws.Cell(noteRow, ColStart).Style.Font.Italic = true;
            ws.Cell(noteRow, ColStart).Style.Font.FontSize = 9;
            ws.Cell(noteRow, ColStart).Style.Font.FontColor = XLColor.DarkGray;
            ws.Cell(noteRow, ColStart).Style.Fill.BackgroundColor = XLColor.FromHtml("#F9FAFB");
            ws.Cell(noteRow, ColStart).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            ws.Cell(noteRow, ColStart).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Cell(noteRow, ColStart).Style.Border.OutsideBorderColor = ColourBorder;
            ws.Row(noteRow).Height = 20;
            row++;

            int headerRow = row;
            WriteTableHeader(ws, row, ColStart, new[]
            {
                "Flat No",
                "Owner Name",
                "Previous Balance",
                "Monthly Charges",
                "Total Due (Before Payment)",
                "Amount Paid",
                "Outstanding",
                "Status"
            });
            ws.SheetView.FreezeRows(row); // freeze through table header row
            row++;

            int dataStart = row;
            foreach (var flat in data.FlatDetails ?? new List<FlatDetailDto>())
            {
                var outstanding = flat.BalanceAmount;
                bool isAlternate = (row - dataStart) % 2 == 1; // Alternate every other row

                ws.Cell(row, ColStart + 0).Value = flat.FlatNo;
                ws.Cell(row, ColStart + 1).Value = flat.OwnerName ?? "-";
                ws.Cell(row, ColStart + 2).Value = flat.OpeningBalance;   // Previous Balance
                ws.Cell(row, ColStart + 3).Value = flat.CurrentBill;      // Monthly Charges
                ws.Cell(row, ColStart + 4).Value = flat.TotalDue;         // Total Due
                ws.Cell(row, ColStart + 5).Value = flat.CurrentPaid;      // Amount Paid
                ws.Cell(row, ColStart + 6).Value = outstanding;           // Outstanding
                ws.Range(row, ColStart + 2, row, ColStart + 6).Style.NumberFormat.Format = AmountFormat;
                ws.Range(row, ColStart + 2, row, ColStart + 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                if (flat.CurrentPaid > 0)
                {
                    ws.Cell(row, ColStart + 5).Style.Font.FontColor = ColourPaidText;
                    ws.Cell(row, ColStart + 5).Style.Font.Bold = true;
                }

                if (outstanding > 0)
                {
                    ws.Cell(row, ColStart + 6).Style.Font.FontColor = ColourPendingText;
                    ws.Cell(row, ColStart + 6).Style.Font.Bold = true;
                }
                else if (outstanding < 0)
                {
                    ws.Cell(row, ColStart + 6).Style.Font.FontColor = ColourPaidText;
                    ws.Cell(row, ColStart + 6).Style.Font.Bold = true;
                }

                var statusCell = ws.Cell(row, ColStart + 7);
                statusCell.Value = FormatMonthlyStatus(flat.Status);
                statusCell.Style.Font.Bold = true;
                statusCell.Style.Font.FontColor = GetMonthlyStatusColor(flat.Status, outstanding);
                statusCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ApplyRowBorder(ws, row, ColStart, colEnd, isAlternate);
                row++;
            }
            int dataEnd = row - 1;

            if (dataEnd >= dataStart)
            {
                // Previous Balance (col+2) and Total Due (col+4) are not additive across flats — omit from totals.
                ApplyTotalRow(ws, row, ColStart, colEnd, new Dictionary<int, string>
                {
                    [ColStart + 3] = $"SUM({ws.Cell(dataStart, ColStart + 3).Address}:{ws.Cell(dataEnd, ColStart + 3).Address})",
                    [ColStart + 5] = $"SUM({ws.Cell(dataStart, ColStart + 5).Address}:{ws.Cell(dataEnd, ColStart + 5).Address})",
                    [ColStart + 6] = $"SUM({ws.Cell(dataStart, ColStart + 6).Address}:{ws.Cell(dataEnd, ColStart + 6).Address})",
                });
                ws.Range(row, ColStart + 3, row, ColStart + 3).Style.NumberFormat.Format = AmountFormat;
                ws.Range(row, ColStart + 3, row, ColStart + 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                ws.Range(row, ColStart + 5, row, ColStart + 6).Style.NumberFormat.Format = AmountFormat;
                ws.Range(row, ColStart + 5, row, ColStart + 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                row++;
            }

            ws.Range(headerRow, ColStart, row - 1, colEnd).SetAutoFilter();
            FinalizeSheet(ws, ColStart, colEnd);
        }

        private static string FormatMonthlyStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return "-";

            var normalized = status.Trim().Replace("_", " ");
            return string.Join(' ', normalized
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
        }

        private static XLColor GetMonthlyStatusColor(string? status, decimal closingBalance)
        {
            var normalized = (status ?? string.Empty).Trim().ToUpperInvariant();

            if (normalized.Contains("ADVANCE") || normalized.Contains("PAID") || closingBalance < 0)
                return ColourPaidText;

            if (normalized.Contains("PARTIAL") || normalized.Contains("PENDING") || normalized.Contains("UNPAID") || normalized.Contains("DUE") || normalized.Contains("ARREAR") || closingBalance > 0)
                return ColourPendingText;

            return XLColor.Black;
        }

        private static void BuildMonthlyExpensesSheet(XLWorkbook wb, MonthlyReportDto data)
        {
            // Data cols: C(3)=Category  D(4)=Amount; title/headers span C:G for visual balance
            const int colEnd     = ColStart + 4; // column G (used for title & section headers)
            const int dataColEnd = ColStart + 1; // column D (used for data rows & total)
            var ws = wb.AddWorksheet("Expenses");
            SetSheetDefaults(ws);
            int row = 1;

            row = WriteReportTitle(ws, row, ColStart, colEnd, data.SocietyName, $"Expenses - {data.PeriodLabel}");

            WriteTableHeader(ws, row, ColStart, new[] { "Category", "Amount" });
            ws.SheetView.FreezeRows(row); // freeze through table header row
            row++;

            var expenses = (data.Expenses ?? new List<ExpenseDto>())
                .OrderByDescending(e => e.TotalAmount)
                .ToList();
            int dataStart = row;
            foreach (var exp in expenses)
            {
                bool isAlternate = (row - dataStart) % 2 == 1; // Alternate every other row
                
                ws.Cell(row, ColStart).Value     = exp.CategoryName;
                ws.Cell(row, ColStart + 1).Value = exp.TotalAmount;
                ws.Cell(row, ColStart + 1).Style.NumberFormat.Format = AmountFormat;
                ws.Cell(row, ColStart + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                ApplyRowBorder(ws, row, ColStart, dataColEnd, isAlternate);
                row++;
            }
            int dataEnd = row - 1;

            if (dataEnd >= dataStart)
            {
                // Compute explicit total to ensure the value is visible in the generated file
                var total = expenses.Sum(e => e.TotalAmount);
                ApplyTotalRow(ws, row, ColStart, dataColEnd, new Dictionary<int, string>
                {
                    // keep a formula for Excel if desired, but we'll overwrite it with the computed value
                    [ColStart + 1] = $"SUM({ws.Cell(dataStart, ColStart + 1).Address}:{ws.Cell(dataEnd, ColStart + 1).Address})",
                });
                // Overwrite formula with computed numeric value so the amount always shows
                ws.Cell(row, ColStart + 1).Value = total;
                ws.Cell(row, ColStart + 1).Style.NumberFormat.Format = AmountFormat;
                ws.Cell(row, ColStart + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            }

            FinalizeSheet(ws, ColStart, colEnd);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Yearly sheet builders
        // ─────────────────────────────────────────────────────────────────────

        private static void BuildYearlyOverviewSheet(XLWorkbook wb, YearlyReportDto data)
        {
            // Content occupies C(3)–G(7): label col C, value col D, headers/alerts span C:G
            const int colEnd = ColStart + 4; // column G
            var ws = wb.AddWorksheet("Overview");
            SetSheetDefaults(ws);
            int row = 1;

            row = WriteReportTitle(ws, row, ColStart, colEnd, data.SocietyName, $"Annual Report - {data.YearLabel}");
            ws.SheetView.FreezeRows(2); // freeze branded title rows 1-2

            // Fund Position
            row = WriteSectionHeader(ws, row, ColStart, colEnd, "Fund Position");
            row = WriteKvRow(ws, row, ColStart, "Opening Balance", data.FundPosition.OpeningBalance,  isAmount: true);
            row = WriteKvRow(ws, row, ColStart, "Total Billed",    data.FundPosition.TotalBilled,     isAmount: true);
            row = WriteKvRow(ws, row, ColStart, "Total Collected", data.FundPosition.TotalCollected,  isAmount: true, valueColor: ColourPaidText);
            row = WriteKvRow(ws, row, ColStart, "Total Expenses",  data.FundPosition.TotalExpenses,   isAmount: true);
            row = WriteKvRow(ws, row, ColStart, "Closing Balance", data.FundPosition.ClosingBalance,  isAmount: true, bold: true, highlight: true);
            row++;

            // Alerts
            row = WriteSectionHeader(ws, row, ColStart, colEnd, "Alerts");
            if (data.Alerts?.Count > 0)
            {
                foreach (var alert in data.Alerts)
                    row = WriteAlertRow(ws, row, ColStart, colEnd, alert, isWarning: true);
            }
            else
            {
                row = WriteAlertRow(ws, row, ColStart, colEnd, "All payments completed", isWarning: false);
            }
            row++;

            // Summary
            if (!string.IsNullOrWhiteSpace(data.Summary))
            {
                row = WriteSectionHeader(ws, row, ColStart, colEnd, "Summary");
                ws.Range(row, ColStart, row, colEnd).Merge();
                ws.Cell(row, ColStart).Value = data.Summary;
                ws.Cell(row, ColStart).Style.Fill.BackgroundColor = XLColor.FromHtml("#F0FDF4");
                ws.Cell(row, ColStart).Style.Alignment.WrapText = true;
                ws.Cell(row, ColStart).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                ws.Cell(row, ColStart).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                ws.Cell(row, ColStart).Style.Font.FontSize = DataSize;
                ws.Cell(row, ColStart).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                ws.Cell(row, ColStart).Style.Border.OutsideBorderColor = ColourBorder;
                ws.Row(row).Height = 40;
                row += 2;
            }

            FinalizeSheet(ws, ColStart, colEnd);
        }

        private static void BuildYearlyMonthlySummarySheet(XLWorkbook wb, YearlyReportDto data)
        {
            // C(3)=Month D(4)=Billed E(5)=Collected F(6)=Expenses G(7)=Net H(8)=Status
            const int colEnd = ColStart + 5; // column H
            var ws = wb.AddWorksheet("Monthly Summary");
            SetSheetDefaults(ws);
            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape; // Landscape for wider table
            int row = 1;

            row = WriteReportTitle(ws, row, ColStart, colEnd, data.SocietyName, $"Monthly Summary - {data.YearLabel}");

            int headerRow = row;
            WriteTableHeader(ws, row, ColStart, new[] { "Month", "Billed", "Collected", "Expenses", "Net", "Status" });
            ws.SheetView.FreezeRows(row); // freeze through table header row
            row++;

            int dataStart = row;
            foreach (var m in data.MonthSummary ?? new List<MonthSummaryDto>())
            {
                bool isAlternate = (row - dataStart) % 2 == 1; // Alternate every other row
                
                ws.Cell(row, ColStart + 0).Value = m.MonthLabel;
                ws.Cell(row, ColStart + 1).Value = m.Billed;
                ws.Cell(row, ColStart + 2).Value = m.Collected;
                ws.Cell(row, ColStart + 3).Value = m.Expenses;
                ws.Cell(row, ColStart + 4).Value = m.Net;
                ws.Range(row, ColStart + 1, row, ColStart + 4).Style.NumberFormat.Format = AmountFormat;
                ws.Range(row, ColStart + 1, row, ColStart + 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                ws.Cell(row, ColStart + 4).Style.Font.Bold = true;

                // Use MonthStatus from SQL if present; fall back to deriving from Net
                var rawStatus = m.MonthStatus?.Trim().ToLowerInvariant();
                bool isSurplus = rawStatus == "surplus" || (rawStatus == null && m.Net >= 0);
                var statusCell = ws.Cell(row, ColStart + 5);
                statusCell.Value = isSurplus ? "Surplus" : "Deficit";
                statusCell.Style.Font.Bold = true;
                statusCell.Style.Font.FontColor = isSurplus ? ColourPaidText : ColourPendingText;
                statusCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ApplyRowBorder(ws, row, ColStart, colEnd, isAlternate);
                row++;
            }
            int dataEnd = row - 1;

            if (dataEnd >= dataStart)
            {
                // Billed (col+1), Collected (col+2), Expenses (col+3), Net (col+4) are summable
                ApplyTotalRow(ws, row, ColStart, colEnd, new Dictionary<int, string>
                {
                    [ColStart + 1] = $"SUM({ws.Cell(dataStart, ColStart + 1).Address}:{ws.Cell(dataEnd, ColStart + 1).Address})",
                    [ColStart + 2] = $"SUM({ws.Cell(dataStart, ColStart + 2).Address}:{ws.Cell(dataEnd, ColStart + 2).Address})",
                    [ColStart + 3] = $"SUM({ws.Cell(dataStart, ColStart + 3).Address}:{ws.Cell(dataEnd, ColStart + 3).Address})",
                    [ColStart + 4] = $"SUM({ws.Cell(dataStart, ColStart + 4).Address}:{ws.Cell(dataEnd, ColStart + 4).Address})",
                });
                ws.Range(row, ColStart + 1, row, ColStart + 4).Style.NumberFormat.Format = AmountFormat;
                ws.Range(row, ColStart + 1, row, ColStart + 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                row++;
            }

            ws.Range(headerRow, ColStart, row - 1, colEnd).SetAutoFilter();
            FinalizeSheet(ws, ColStart, colEnd);
        }

        private static void BuildYearlyExpensesSheet(XLWorkbook wb, YearlyReportDto data)
        {
            // Data cols: C(3)=Category  D(4)=Amount; title/headers span C:G for visual balance
            const int colEnd     = ColStart + 4; // column G (used for title & section headers)
            const int dataColEnd = ColStart + 1; // column D (used for data rows & total)
            var ws = wb.AddWorksheet("Expenses");
            SetSheetDefaults(ws);
            int row = 1;

            row = WriteReportTitle(ws, row, ColStart, colEnd, data.SocietyName, $"Expenses - {data.YearLabel}");

            WriteTableHeader(ws, row, ColStart, new[] { "Category", "Total Amount" });
            ws.SheetView.FreezeRows(row); // freeze through table header row
            row++;

            // Sort by highest amount
            var expenses = (data.Expenses ?? new List<ExpenseDto>())
                .OrderByDescending(e => e.TotalAmount)
                .ToList();

            int dataStart = row;
            foreach (var exp in expenses)
            {
                bool isAlternate = (row - dataStart) % 2 == 1; // Alternate every other row
                
                ws.Cell(row, ColStart).Value     = exp.CategoryName;
                ws.Cell(row, ColStart + 1).Value = exp.TotalAmount;
                ws.Cell(row, ColStart + 1).Style.NumberFormat.Format = AmountFormat;
                ws.Cell(row, ColStart + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                ApplyRowBorder(ws, row, ColStart, dataColEnd, isAlternate);
                row++;
            }
            int dataEnd = row - 1;

            if (dataEnd >= dataStart)
            {
                // Compute explicit total to ensure the value is visible in the generated file
                var total = expenses.Sum(e => e.TotalAmount);
                ApplyTotalRow(ws, row, ColStart, dataColEnd, new Dictionary<int, string>
                {
                    // keep a formula for Excel if desired, but we'll overwrite it with the computed value
                    [ColStart + 1] = $"SUM({ws.Cell(dataStart, ColStart + 1).Address}:{ws.Cell(dataEnd, ColStart + 1).Address})",
                });
                // Overwrite formula with computed numeric value so the amount always shows
                ws.Cell(row, ColStart + 1).Value = total;
                ws.Cell(row, ColStart + 1).Style.NumberFormat.Format = AmountFormat;
                ws.Cell(row, ColStart + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            }

            FinalizeSheet(ws, ColStart, colEnd);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Shared helpers
        // ─────────────────────────────────────────────────────────────────────

        private static void SetSheetDefaults(IXLWorksheet ws)
        {
            ws.Style.Font.FontName = FontName;
            ws.Style.Font.FontSize = DataSize;
            
            // Enhanced print settings for professional appearance
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.PageSetup.CenterHorizontally = true;
            ws.PageSetup.CenterVertically = false;
            ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            ws.PageSetup.FitToPages(1, 0); // fit to 1 page wide, unlimited height
            ws.PageSetup.Margins.Top = 0.5;
            ws.PageSetup.Margins.Bottom = 0.5;
            ws.PageSetup.Margins.Left = 0.5;
            ws.PageSetup.Margins.Right = 0.5;
            ws.PageSetup.Margins.Header = 0.3;
            ws.PageSetup.Margins.Footer = 0.3;
            ws.PageSetup.PrintAreas.Clear();
            ws.ShowGridLines = false; // Hide grid lines for cleaner appearance
        }

        /// Writes a two-row branded title block (society name + subtitle). Returns next row.
        private static int WriteReportTitle(IXLWorksheet ws, int row, int colStart, int colEnd,
            string societyName, string subtitle)
        {
            // Row 1: Society Name — merged C:colEnd
            ws.Range(row, colStart, row, colEnd).Merge();
            var titleCell = ws.Cell(row, colStart);
            titleCell.Value = societyName;
            titleCell.Style.Font.FontSize = TitleFontSize;
            titleCell.Style.Font.Bold = true;
            titleCell.Style.Font.FontColor = XLColor.White;
            titleCell.Style.Fill.BackgroundColor = ColourHeaderBg;
            titleCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            titleCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            titleCell.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            titleCell.Style.Border.OutsideBorderColor = ColourBorderDark;
            ws.Row(row).Height = 40;
            row++;

            // Row 2: Subtitle — merged C:colEnd
            ws.Range(row, colStart, row, colEnd).Merge();
            var subCell = ws.Cell(row, colStart);
            subCell.Value = subtitle;
            subCell.Style.Font.FontSize = SubtitleSize;
            subCell.Style.Font.Bold = true;
            subCell.Style.Font.FontColor = XLColor.White;
            subCell.Style.Fill.BackgroundColor = ColourSectionBg;
            subCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            subCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            subCell.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            subCell.Style.Border.OutsideBorderColor = ColourBorderDark;
            ws.Row(row).Height = 28;
            row += 2; // blank spacing after title

            return row;
        }

        /// Writes an emerald section header spanning colStart:colEnd. Returns next row.
        private static int WriteSectionHeader(IXLWorksheet ws, int row, int colStart, int colEnd, string title)
        {
            ws.Range(row, colStart, row, colEnd).Merge();
            var cell = ws.Cell(row, colStart);
            cell.Value = $"  {title}";
            cell.Style.Font.FontSize = SectionSize;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = ColourSectionBg;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            cell.Style.Border.OutsideBorderColor = ColourBorderDark;
            ws.Row(row).Height = 26;
            return row + 1;
        }

        /// Writes a label/value row: label at colStart, value at colStart+1. Returns next row.
        private static int WriteKvRow(IXLWorksheet ws, int row, int colStart, string label, object value,
            bool isAmount = false, bool bold = false, bool highlight = false,
            XLColor? valueColor = null)
        {
            var labelCell = ws.Cell(row, colStart);
            labelCell.Value = $"  {label}";
            labelCell.Style.Font.Bold = bold;
            labelCell.Style.Font.FontSize = DataSize;
            labelCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            labelCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            var valueCell = ws.Cell(row, colStart + 1);
            switch (value)
            {
                case decimal d: valueCell.Value = d; break;
                case int     i: valueCell.Value = i; break;
                case long    l: valueCell.Value = l; break;
                default:        valueCell.Value = value?.ToString() ?? string.Empty; break;
            }
            valueCell.Style.Font.Bold = bold;
            valueCell.Style.Font.FontSize = DataSize;
            valueCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            valueCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            if (isAmount)
                valueCell.Style.NumberFormat.Format = AmountFormat;

            if (valueColor != null)
                valueCell.Style.Font.FontColor = valueColor;

            // Enhanced card-style background with borders
            var bg = highlight ? ColourHighlightBg : ColourCardBg;
            var cellRange = ws.Range(row, colStart, row, colStart + 1);
            cellRange.Style.Fill.BackgroundColor = bg;
            cellRange.Style.Border.TopBorder = XLBorderStyleValues.Thin;
            cellRange.Style.Border.TopBorderColor = ColourBorder;
            cellRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            cellRange.Style.Border.BottomBorderColor = ColourBorder;
            cellRange.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
            cellRange.Style.Border.LeftBorderColor = ColourBorder;
            cellRange.Style.Border.RightBorder = XLBorderStyleValues.Thin;
            cellRange.Style.Border.RightBorderColor = ColourBorder;
            
            ws.Row(row).Height = 22;
            return row + 1;
        }

        /// Writes a merged alert row spanning colStart:colEnd. Returns next row.
        private static int WriteAlertRow(IXLWorksheet ws, int row, int colStart, int colEnd,
            string message, bool isWarning)
        {
            ws.Range(row, colStart, row, colEnd).Merge();
            var cell = ws.Cell(row, colStart);
            cell.Value = $"  {message}";
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontSize = DataSize;
            cell.Style.Font.FontColor = isWarning ? ColourPendingText : ColourPaidText;
            cell.Style.Fill.BackgroundColor = isWarning ? ColourPendingBg : XLColor.FromHtml("#F0FDF4");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = isWarning ? ColourPendingText : ColourPaidText;
            ws.Row(row).Height = 24;
            return row + 1;
        }

        /// Writes an emerald-headed table header row starting at colStart.
        private static void WriteTableHeader(IXLWorksheet ws, int row, int colStart, string[] headers)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(row, colStart + i);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontSize = DataSize;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = ColourHeaderBg;
                cell.Style.Alignment.Horizontal = i == 0
                    ? XLAlignmentHorizontalValues.Left
                    : XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                cell.Style.Border.OutsideBorderColor = ColourBorderDark;
            }
            ws.Row(row).Height = 28;
        }

        /// Applies borders and optional alternating row color to a data row from colStart to colEnd.
        private static void ApplyRowBorder(IXLWorksheet ws, int row, int colStart, int colEnd, bool isAlternate = false)
        {
            var rowRange = ws.Range(row, colStart, row, colEnd);
            
            // Apply alternating row background for better readability
            if (isAlternate)
                rowRange.Style.Fill.BackgroundColor = ColourAlternateRow;
            
            // Apply borders to all cells in the row
            rowRange.Style.Border.TopBorder = XLBorderStyleValues.Thin;
            rowRange.Style.Border.TopBorderColor = ColourBorder;
            rowRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            rowRange.Style.Border.BottomBorderColor = ColourBorder;
            rowRange.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
            rowRange.Style.Border.LeftBorderColor = ColourBorder;
            rowRange.Style.Border.RightBorder = XLBorderStyleValues.Thin;
            rowRange.Style.Border.RightBorderColor = ColourBorder;
            
            // Vertical alignment
            rowRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            
            ws.Row(row).Height = 22;
        }

        /// Writes a bold emerald total row with SUM formulas from colStart to colEnd.
        private static void ApplyTotalRow(IXLWorksheet ws, int row, int colStart, int colEnd,
            Dictionary<int, string> formulas)
        {
            var rowRange = ws.Range(row, colStart, row, colEnd);
            rowRange.Style.Fill.BackgroundColor = ColourTotalRowBg;
            rowRange.Style.Font.Bold = true;
            rowRange.Style.Font.FontSize = DataSize;
            rowRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            
            // Strong borders for emphasis
            rowRange.Style.Border.TopBorder = XLBorderStyleValues.Medium;
            rowRange.Style.Border.TopBorderColor = ColourBorderDark;
            rowRange.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
            rowRange.Style.Border.BottomBorderColor = ColourBorderDark;
            rowRange.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
            rowRange.Style.Border.LeftBorderColor = ColourBorder;
            rowRange.Style.Border.RightBorder = XLBorderStyleValues.Thin;
            rowRange.Style.Border.RightBorderColor = ColourBorder;
            
            ws.Cell(row, colStart).Value = "Total";
            ws.Cell(row, colStart).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            
            foreach (var (col, formula) in formulas)
                ws.Cell(row, col).FormulaA1 = formula;
            
            ws.Row(row).Height = 26;
        }

        /// Sets narrow left-margin columns, auto-fits content columns, and enforces minimum widths.
        private static void FinalizeSheet(IXLWorksheet ws, int colStart, int colEnd)
        {
            // Narrow left-margin columns (A, B)
            for (int c = 1; c < colStart; c++)
                ws.Column(c).Width = 3;

            foreach (var r in ws.RowsUsed())
                if (r.Height < 18) r.Height = 18;

            // Auto-fit only the content columns
            for (int c = colStart; c <= colEnd; c++)
                ws.Column(c).AdjustToContents();

            // Enforce minimum widths for content columns
            for (int c = colStart; c <= colEnd; c++)
            {
                double minWidth = c == colStart ? 22 : 16;
                if (ws.Column(c).Width < minWidth)
                    ws.Column(c).Width = minWidth;
            }
        }
    }
}