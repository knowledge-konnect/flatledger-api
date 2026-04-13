using ClosedXML.Excel;
using SocietyLedger.Application.DTOs.Reports;
using SocietyLedger.Application.Interfaces.Services;

namespace SocietyLedger.Infrastructure.Services
{
    public class ReportExportService : IReportExportService
    {
        // ── Design tokens ─────────────────────────────────────────────────────
        private const string FontName       = "Calibri";
        private const int    TitleFontSize  = 20;
        private const int    SubtitleSize   = 13;
        private const int    SectionSize    = 11;
        private const int    DataSize       = 11;
        private const string AmountFormat   = "₹#,##0";

        // Layout: A–B are left-margin columns; all content starts at column C (index 3).
        // Right margin begins at column I (index 9) and beyond.
        private const int ColStart = 3; // Column C — first content column

        // Emerald palette
        private static readonly XLColor ColourHeaderBg    = XLColor.FromHtml("#064E3B"); // dark emerald
        private static readonly XLColor ColourSectionBg   = XLColor.FromHtml("#065F46"); // section bg
        private static readonly XLColor ColourLightBg     = XLColor.FromHtml("#D1FAE5"); // light green
        private static readonly XLColor ColourHighlightBg = XLColor.FromHtml("#A7F3D0"); // closing-balance
        private static readonly XLColor ColourBorder      = XLColor.FromHtml("#E5E7EB"); // borders
        private static readonly XLColor ColourPaidText    = XLColor.FromHtml("#059669"); // paid / positive
        private static readonly XLColor ColourPendingText = XLColor.FromHtml("#DC2626"); // pending / negative
        private static readonly XLColor ColourTotalRowBg  = XLColor.FromHtml("#D1FAE5"); // total row

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
            row = WriteKvRow(ws, row, ColStart, "Pending",     ps.Pending, valueColor: ps.Pending > 0 ? ColourPendingText : null);
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
                ws.Cell(row, ColStart).Style.Fill.BackgroundColor = ColourLightBg;
                ws.Cell(row, ColStart).Style.Alignment.WrapText = true;
                ws.Cell(row, ColStart).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                ws.Row(row).Height = 36;
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
            int row = 1;

            row = WriteReportTitle(ws, row, ColStart, colEnd, data.SocietyName, $"Payments - {data.PeriodLabel}");

            int noteRow = row;
            // small explanatory note for users clarifying column meanings
            ws.Range(noteRow, ColStart, noteRow, colEnd).Merge();
            ws.Cell(noteRow, ColStart).Value = "Note: 'Total Due (Before Payment)' = Previous Balance + Monthly Charges. 'Outstanding' = Total Due - Amount Paid.";
            ws.Cell(noteRow, ColStart).Style.Font.Italic = true;
            ws.Cell(noteRow, ColStart).Style.Font.FontColor = XLColor.DarkGray;
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
            row++;

            int dataStart = row;
            foreach (var flat in data.FlatDetails ?? new List<FlatDetailDto>())
            {
                var outstanding = flat.BalanceAmount;

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

                ApplyRowBorder(ws, row, ColStart, colEnd);
                row++;
            }
            int dataEnd = row - 1;

            if (dataEnd >= dataStart)
            {
                ApplyTotalRow(ws, row, ColStart, colEnd, new Dictionary<int, string>
                {
                    [ColStart + 2] = $"SUM({ws.Cell(dataStart, ColStart + 2).Address}:{ws.Cell(dataEnd, ColStart + 2).Address})",
                    [ColStart + 3] = $"SUM({ws.Cell(dataStart, ColStart + 3).Address}:{ws.Cell(dataEnd, ColStart + 3).Address})",
                    [ColStart + 4] = $"SUM({ws.Cell(dataStart, ColStart + 4).Address}:{ws.Cell(dataEnd, ColStart + 4).Address})",
                    [ColStart + 5] = $"SUM({ws.Cell(dataStart, ColStart + 5).Address}:{ws.Cell(dataEnd, ColStart + 5).Address})",
                    [ColStart + 6] = $"SUM({ws.Cell(dataStart, ColStart + 6).Address}:{ws.Cell(dataEnd, ColStart + 6).Address})",
                });
                ws.Range(row, ColStart + 2, row, ColStart + 6).Style.NumberFormat.Format = AmountFormat;
                ws.Range(row, ColStart + 2, row, ColStart + 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
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
            row++;

            var expenses = data.Expenses ?? new List<ExpenseDto>();
            int dataStart = row;
            foreach (var exp in expenses)
            {
                ws.Cell(row, ColStart).Value     = exp.CategoryName;
                ws.Cell(row, ColStart + 1).Value = exp.TotalAmount;
                ws.Cell(row, ColStart + 1).Style.NumberFormat.Format = AmountFormat;
                ws.Cell(row, ColStart + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                ApplyRowBorder(ws, row, ColStart, dataColEnd);
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

            // Fund Position
            row = WriteSectionHeader(ws, row, ColStart, colEnd, "Fund Position");
            row = WriteKvRow(ws, row, ColStart, "Opening Balance", data.FundPosition.OpeningBalance,  isAmount: true);
            row = WriteKvRow(ws, row, ColStart, "Collected",       data.FundPosition.TotalCollected,  isAmount: true);
            row = WriteKvRow(ws, row, ColStart, "Expenses",        data.FundPosition.TotalExpenses,   isAmount: true);
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
                ws.Cell(row, ColStart).Style.Fill.BackgroundColor = ColourLightBg;
                ws.Cell(row, ColStart).Style.Alignment.WrapText = true;
                ws.Cell(row, ColStart).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                ws.Row(row).Height = 36;
                row += 2;
            }

            FinalizeSheet(ws, ColStart, colEnd);
        }

        private static void BuildYearlyMonthlySummarySheet(XLWorkbook wb, YearlyReportDto data)
        {
            // C(3)=Month  D(4)=Collected  E(5)=Expenses  F(6)=Net  G(7)=Status
            const int colEnd = ColStart + 4; // column G
            var ws = wb.AddWorksheet("Monthly Summary");
            SetSheetDefaults(ws);
            int row = 1;

            row = WriteReportTitle(ws, row, ColStart, colEnd, data.SocietyName, $"Monthly Summary - {data.YearLabel}");

            int headerRow = row;
            WriteTableHeader(ws, row, ColStart, new[] { "Month", "Collected", "Expenses", "Net", "Status" });
            row++;

            int dataStart = row;
            foreach (var m in data.MonthSummary ?? new List<MonthSummaryDto>())
            {
                ws.Cell(row, ColStart + 0).Value = m.MonthLabel;
                ws.Cell(row, ColStart + 1).Value = m.Collected;
                ws.Cell(row, ColStart + 2).Value = m.Expenses;
                ws.Cell(row, ColStart + 3).Value = m.Net;
                ws.Range(row, ColStart + 1, row, ColStart + 3).Style.NumberFormat.Format = AmountFormat;
                ws.Range(row, ColStart + 1, row, ColStart + 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                ws.Cell(row, ColStart + 3).Style.Font.Bold = true;

                bool isBalance = m.Net >= 0;
                var statusCell = ws.Cell(row, ColStart + 4);
                statusCell.Value = isBalance ? "balance left" : "extra spent";
                statusCell.Style.Font.Bold = true;
                statusCell.Style.Font.FontColor = isBalance ? ColourPaidText : ColourPendingText;
                statusCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ApplyRowBorder(ws, row, ColStart, colEnd);
                row++;
            }
            int dataEnd = row - 1;

            if (dataEnd >= dataStart)
            {
                ApplyTotalRow(ws, row, ColStart, colEnd, new Dictionary<int, string>
                {
                    [ColStart + 1] = $"SUM({ws.Cell(dataStart, ColStart + 1).Address}:{ws.Cell(dataEnd, ColStart + 1).Address})",
                    [ColStart + 2] = $"SUM({ws.Cell(dataStart, ColStart + 2).Address}:{ws.Cell(dataEnd, ColStart + 2).Address})",
                    [ColStart + 3] = $"SUM({ws.Cell(dataStart, ColStart + 3).Address}:{ws.Cell(dataEnd, ColStart + 3).Address})",
                });
                ws.Range(row, ColStart + 1, row, ColStart + 3).Style.NumberFormat.Format = AmountFormat;
                ws.Range(row, ColStart + 1, row, ColStart + 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
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
            row++;

            // Sort by highest amount
            var expenses = (data.Expenses ?? new List<ExpenseDto>())
                .OrderByDescending(e => e.TotalAmount)
                .ToList();

            int dataStart = row;
            foreach (var exp in expenses)
            {
                ws.Cell(row, ColStart).Value     = exp.CategoryName;
                ws.Cell(row, ColStart + 1).Value = exp.TotalAmount;
                ws.Cell(row, ColStart + 1).Style.NumberFormat.Format = AmountFormat;
                ws.Cell(row, ColStart + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                ApplyRowBorder(ws, row, ColStart, dataColEnd);
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
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.PageSetup.CenterHorizontally = true;
            ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            ws.PageSetup.FitToPages(1, 0); // fit to 1 page wide, unlimited height
            ws.SheetView.FreezeRows(1);    // freeze top row
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
            ws.Row(row).Height = 36;
            row++;

            // Row 2: Subtitle — merged C:colEnd
            ws.Range(row, colStart, row, colEnd).Merge();
            var subCell = ws.Cell(row, colStart);
            subCell.Value = subtitle;
            subCell.Style.Font.FontSize = SubtitleSize;
            subCell.Style.Font.Bold = true;
            subCell.Style.Font.FontColor = XLColor.White;
            subCell.Style.Fill.BackgroundColor = ColourHeaderBg;
            subCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            subCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(row).Height = 24;
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
            ws.Row(row).Height = 22;
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

            if (isAmount)
                valueCell.Style.NumberFormat.Format = AmountFormat;

            if (valueColor != null)
                valueCell.Style.Font.FontColor = valueColor;

            var bg = highlight ? ColourHighlightBg : ColourLightBg;
            ws.Range(row, colStart, row, colStart + 1).Style.Fill.BackgroundColor = bg;
            ws.Range(row, colStart, row, colStart + 1).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            ws.Range(row, colStart, row, colStart + 1).Style.Border.BottomBorderColor = ColourBorder;
            ws.Row(row).Height = 20;
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
            cell.Style.Fill.BackgroundColor = isWarning
                ? XLColor.FromHtml("#FEF2F2")
                : XLColor.FromHtml("#F0FDF4");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            ws.Row(row).Height = 20;
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
                cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.BottomBorderColor = ColourBorder;
            }
            ws.Row(row).Height = 24;
        }

        /// Applies a thin bottom border to a data row from colStart to colEnd.
        private static void ApplyRowBorder(IXLWorksheet ws, int row, int colStart, int colEnd)
        {
            ws.Range(row, colStart, row, colEnd).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            ws.Range(row, colStart, row, colEnd).Style.Border.BottomBorderColor = ColourBorder;
            ws.Row(row).Height = 20;
        }

        /// Writes a bold light-green total row with SUM formulas from colStart to colEnd.
        private static void ApplyTotalRow(IXLWorksheet ws, int row, int colStart, int colEnd,
            Dictionary<int, string> formulas)
        {
            ws.Range(row, colStart, row, colEnd).Style.Fill.BackgroundColor = ColourTotalRowBg;
            ws.Range(row, colStart, row, colEnd).Style.Font.Bold = true;
            ws.Range(row, colStart, row, colEnd).Style.Border.TopBorder = XLBorderStyleValues.Thin;
            ws.Range(row, colStart, row, colEnd).Style.Border.TopBorderColor = ColourBorder;
            ws.Cell(row, colStart).Value = "Total";
            ws.Cell(row, colStart).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            foreach (var (col, formula) in formulas)
                ws.Cell(row, col).FormulaA1 = formula;
            ws.Row(row).Height = 22;
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