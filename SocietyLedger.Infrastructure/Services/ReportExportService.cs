﻿using ClosedXML.Excel;
using SocietyLedger.Application.DTOs.Reports;
using SocietyLedger.Application.Interfaces.Services;

namespace SocietyLedger.Infrastructure.Services
{
    public class ReportExportService : IReportExportService
    {
        // ── Design tokens ─────────────────────────────────────────────────────
        private const string FontName = "Segoe UI";
        private const string BuildingIcon = "🏢";
        private const int TitleFontSize = 22;
        private const int SubtitleSize = 14;
        private const int SectionSize = 12;
        private const int DataSize = 11;
        private const string AmountFormat = "₹#,##0.00;[Red]-₹#,##0.00";

        // Layout: A–B are left-margin columns; all content starts at column C (index 3).
        // Right margin begins at column I (index 9) and beyond.
        private const int ColStart = 3; // Column C — first content column

        // Emerald palette - Enhanced for professional appearance
        private static readonly XLColor ColourHeaderBg = XLColor.FromHtml("#064E3B"); // dark emerald - main headers
        private static readonly XLColor ColourSectionBg = XLColor.FromHtml("#065F46"); // section headers
        private static readonly XLColor ColourLightBg = XLColor.FromHtml("#D1FAE5"); // light green backgrounds
        private static readonly XLColor ColourHighlightBg = XLColor.FromHtml("#A7F3D0"); // important highlights
        private static readonly XLColor ColourAlternateRow = XLColor.FromHtml("#F0FDF4"); // alternating table rows
        private static readonly XLColor ColourBorder = XLColor.FromHtml("#D1D5DB"); // borders
        private static readonly XLColor ColourBorderDark = XLColor.FromHtml("#9CA3AF"); // stronger borders
        private static readonly XLColor ColourPaidText = XLColor.FromHtml("#059669"); // paid / positive
        private static readonly XLColor ColourPaidBg = XLColor.FromHtml("#ECFDF5"); // paid status background
        private static readonly XLColor ColourPendingText = XLColor.FromHtml("#DC2626"); // pending / negative
        private static readonly XLColor ColourPendingBg = XLColor.FromHtml("#FEF2F2"); // pending status background
        private static readonly XLColor ColourTotalRowBg = XLColor.FromHtml("#A7F3D0"); // total row - darker for emphasis
        private static readonly XLColor ColourCardBg = XLColor.FromHtml("#ECFDF5"); // card-style sections
        private static readonly XLColor ColourWhite = XLColor.White;

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

            row += 1;

            // Fund Position
            row = WriteSectionHeader(ws, row, ColStart, colEnd, "Fund Position");
            row = WriteKvRow(ws, row, ColStart, "Opening Balance", data.FundPosition.OpeningBalance, isAmount: true);
            row = WriteKvRow(ws, row, ColStart, "Collected", data.FundPosition.Collected, isAmount: true);
            row = WriteKvRow(ws, row, ColStart, "Society Other Income", data.PaymentSummary?.OtherIncome ?? 0m, isAmount: true, valueColor: ColourPaidText);
            row = WriteKvRow(ws, row, ColStart, "Expenses", data.FundPosition.Expenses, isAmount: true);
            row = WriteKvRow(ws, row, ColStart, "Closing Balance", data.FundPosition.ClosingBalance, isAmount: true, bold: true, highlight: true);
            row++;

            // Payment Summary
            var ps = data.PaymentSummary ?? new PaymentSummaryDto();
            row = WriteSectionHeader(ws, row, ColStart, colEnd, "Payment Summary");
            row = WriteKvRow(ws, row, ColStart, "Total Flats", ps.TotalFlats);
            row = WriteKvRow(ws, row, ColStart, "Paid", ps.Paid, valueColor: ColourPaidText);
            row = WriteKvRow(ws, row, ColStart, "Pending", ps.Pending, valueColor: ps.Pending > 0 ? ColourPendingText : null);
            row = WriteKvRow(ws, row, ColStart, "Total Billed", ps.TotalBilled, isAmount: true);
            row = WriteKvRow(ws, row, ColStart, "Total Collected", ps.TotalCollected, isAmount: true, valueColor: ColourPaidText);
            row = WriteKvRow(ws, row, ColStart, "Society Other Income", ps.OtherIncome, isAmount: true, valueColor: ColourPaidText);
            row = WriteKvRow(ws, row, ColStart, "Pending Amount", ps.PendingAmount, isAmount: true, valueColor: ps.PendingAmount > 0 ? ColourPendingText : null);
            row = WriteKvRow(ws, row, ColStart, "Collection Efficiency", $"{ps.CollectionEfficiency}%");
            row++;

            // Alerts
            row = WriteSectionHeader(ws, row, ColStart, colEnd, "Alerts");
            if (ps.Pending > 0 && data.Alerts?.Count > 0)
            {
                foreach (var alert in data.Alerts)
                    row = WriteAlertRow(ws, row, ColStart, colEnd, alert, isWarning: true);
            }
            else if (ps.TotalCollected > ps.TotalBilled)
            {
                row = WriteAlertRow(ws, row, ColStart, colEnd, "All maintenance dues cleared. One or more flats have advance / overpayment credit.", isWarning: false);
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
            ApplyPreferredColumnWidths(ws, new Dictionary<int, double>
            {
                [ColStart + 0] = 34, // Label
                [ColStart + 1] = 20, // Value
            });
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
            ws.Cell(noteRow, ColStart).Style.Alignment.WrapText = true;
            ws.Cell(noteRow, ColStart).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Cell(noteRow, ColStart).Style.Border.OutsideBorderColor = ColourBorder;
            ws.Row(noteRow).Height = GetWrappedRowHeight("Note: 'Total Due (Before Payment)' = Previous Balance + Monthly Charges. 'Outstanding' = Total Due - Amount Paid.", colEnd - ColStart + 1);
            row++;

            int headerRow = row;
            WriteTableHeader(ws, row, ColStart, new[]
            {
                "Flat No",
                "Resident Name",
                "Previous Balance",
                "Monthly Charges",
                "Total Due",
                "Amount Paid",
                "Outstanding",
                "Status"
            });
            ws.SheetView.FreezeRows(row); // freeze through table header row
            row++;

            var flatRows = data.FlatDetails ?? new List<FlatDetailDto>();
            if (flatRows.Count == 0)
            {
                row = WriteEmptyTableMessage(ws, row, ColStart, colEnd, "No flat payment data for this period.");
            }
            else
            {
                int dataStart = row;
                foreach (var flat in flatRows)
                {
                    var outstanding = flat.BalanceAmount;
                    bool isAlternate = (row - dataStart) % 2 == 1; // Alternate every other row

                    ws.Cell(row, ColStart + 0).Value = flat.FlatNo;
                    ws.Cell(row, ColStart + 1).Value = GetResidentDisplayName(flat.TenantName, flat.OwnerName);
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
            }

            var otherIncomeRows = (data.IncomeDetails ?? new List<IncomeDetailDto>())
                .OrderBy(i => i.DatePaid)
                .ThenBy(i => i.CategoryName)
                .ToList();

            if (otherIncomeRows.Count > 0)
            {
                row += 2;
                const int incomeColEnd = ColStart + 5; // columns C:H
                var incomeHeaderRow = row;
                ws.Range(row, ColStart, row, incomeColEnd).Merge();
                var incomeTitle = "Society Other Income Details (Not Maintenance Dues)";
                ws.Cell(row, ColStart).Value = incomeTitle;
                ws.Cell(row, ColStart).Style.Font.Bold = true;
                ws.Cell(row, ColStart).Style.Font.FontColor = XLColor.White;
                ws.Cell(row, ColStart).Style.Fill.BackgroundColor = ColourSectionBg;
                ws.Cell(row, ColStart).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                ws.Cell(row, ColStart).Style.Alignment.WrapText = true;
                ws.Cell(row, ColStart).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                ws.Cell(row, ColStart).Style.Border.OutsideBorderColor = ColourBorderDark;
                ws.Row(row).Height = GetWrappedRowHeight(incomeTitle, incomeColEnd - ColStart + 1, 30);
                row++;

                WriteTableHeader(ws, row, ColStart, new[] { "Flat No", "Owner / Tenant", "Date", "Category", "Description", "Amount" });
                int incomeDataStart = row + 1;
                int incomeDataEnd = incomeDataStart - 1;
                row++;

                foreach (var income in otherIncomeRows)
                {
                    bool isAlternate = (row - incomeDataStart) % 2 == 1;
                    ws.Cell(row, ColStart).Value = string.IsNullOrWhiteSpace(income.FlatNo) ? "-" : income.FlatNo;
                    // Prefer tenant name (if present) for the resident column, else fallback to owner name
                    var resident = string.IsNullOrWhiteSpace(income.TenantName)
                        ? (string.IsNullOrWhiteSpace(income.OwnerName) ? "-" : income.OwnerName)
                        : income.TenantName;
                    ws.Cell(row, ColStart + 1).Value = resident;
                    ws.Cell(row, ColStart + 2).Value = income.DatePaid.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.Zero));
                    ws.Cell(row, ColStart + 2).Style.DateFormat.Format = "dd-mmm-yyyy";
                    ws.Cell(row, ColStart + 3).Value = income.CategoryName;
                    ws.Cell(row, ColStart + 4).Value = string.IsNullOrWhiteSpace(income.Description) ? "-" : income.Description;
                    ws.Cell(row, ColStart + 4).Style.Alignment.WrapText = true;
                    ws.Cell(row, ColStart + 4).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                    ws.Cell(row, ColStart + 5).Value = income.Amount;
                    ws.Cell(row, ColStart + 5).Style.NumberFormat.Format = AmountFormat;
                    ws.Cell(row, ColStart + 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    ApplyRowBorder(ws, row, ColStart, incomeColEnd, isAlternate);
                    incomeDataEnd = row;
                    row++;
                }

                if (incomeDataEnd >= incomeDataStart)
                {
                    var total = otherIncomeRows.Sum(i => i.Amount);
                    ApplyTotalRow(ws, row, ColStart, incomeColEnd, new Dictionary<int, string>
                    {
                        [ColStart + 5] = $"SUM({ws.Cell(incomeDataStart, ColStart + 5).Address}:{ws.Cell(incomeDataEnd, ColStart + 5).Address})",
                    });
                    ws.Cell(row, ColStart + 5).Value = total;
                    ws.Cell(row, ColStart + 5).Style.NumberFormat.Format = AmountFormat;
                    ws.Cell(row, ColStart + 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    row++;
                }

                ws.Range(incomeHeaderRow, ColStart, row - 1, incomeColEnd).SetAutoFilter();
                ApplyPreferredColumnWidths(ws, new Dictionary<int, double>
                {
                    [ColStart + 0] = 12,
                    [ColStart + 1] = 28,
                    [ColStart + 2] = 16,
                    [ColStart + 3] = 22,
                    [ColStart + 4] = 38,
                    [ColStart + 5] = 22,
                });
            }

            //if (flatRows.Count > 0)
            //{
            //    ws.Range(headerRow, ColStart, row - 1, colEnd).SetAutoFilter();
            //}
            FinalizeSheet(ws, ColStart, colEnd);
            ApplyPreferredColumnWidths(ws, new Dictionary<int, double>
            {
                [ColStart + 0] = 12, // Flat No
                [ColStart + 1] = 30, // Resident Name
                [ColStart + 2] = 20, // Previous Balance
                [ColStart + 3] = 20, // Monthly Charges
                [ColStart + 4] = 30, // Total Due
                [ColStart + 5] = 20, // Amount Paid
                [ColStart + 6] = 20, // Outstanding
                [ColStart + 7] = 16, // Status
            });
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
            // Data cols: C(3)=Date D(4)=Category E(5)=Description F(6)=Amount; title/headers span C:H for visual balance
            const int colEnd = ColStart + 5; // column H (used for title & section headers)
            const int dataColEnd = ColStart + 3; // column F (used for data rows & total)
            var ws = wb.AddWorksheet("Expenses");
            SetSheetDefaults(ws);
            int row = 1;

            row = WriteReportTitle(ws, row, ColStart, colEnd, data.SocietyName, $"Expenses - {data.PeriodLabel}");

            int headerRow = row;
            WriteTableHeader(ws, row, ColStart, new[] { "Date", "Category", "Description", "Amount" });
            ws.SheetView.FreezeRows(row); // freeze through table header row
            row++;

            var expenses = (data.Expenses ?? new List<ExpenseDto>())
                .OrderBy(e => e.DateIncurred)
                .ThenBy(e => e.CategoryName)
                .ToList();

            if (expenses.Count == 0)
            {
                row = WriteEmptyTableMessage(ws, row, ColStart, dataColEnd, "No expense entries recorded for this period.");
            }
            else
            {
                int dataStart = row;
                foreach (var exp in expenses)
                {
                    bool isAlternate = (row - dataStart) % 2 == 1; // Alternate every other row

                    ws.Cell(row, ColStart).Value = exp.DateIncurred.ToDateTime(TimeOnly.MinValue);
                    ws.Cell(row, ColStart).Style.DateFormat.Format = "dd-mmm-yyyy";
                    ws.Cell(row, ColStart + 1).Value = exp.CategoryName;
                    ws.Cell(row, ColStart + 2).Value = string.IsNullOrWhiteSpace(exp.Description) ? "-" : exp.Description;
                    ws.Cell(row, ColStart + 2).Style.Alignment.WrapText = true;
                    ws.Cell(row, ColStart + 2).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                    ws.Cell(row, ColStart + 3).Value = exp.TotalAmount;
                    ws.Cell(row, ColStart + 3).Style.NumberFormat.Format = AmountFormat;
                    ws.Cell(row, ColStart + 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
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
                        [ColStart + 3] = $"SUM({ws.Cell(dataStart, ColStart + 3).Address}:{ws.Cell(dataEnd, ColStart + 3).Address})",
                    });
                    // Overwrite formula with computed numeric value so the amount always shows
                    ws.Cell(row, ColStart + 3).Value = total;
                    ws.Cell(row, ColStart + 3).Style.NumberFormat.Format = AmountFormat;
                    ws.Cell(row, ColStart + 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    row++;
                }

                ws.Range(headerRow, ColStart, row - 1, dataColEnd).SetAutoFilter();
            }

            FinalizeSheet(ws, ColStart, colEnd);
            ApplyPreferredColumnWidths(ws, new Dictionary<int, double>
            {
                [ColStart + 0] = 14, // Date
                [ColStart + 1] = 22, // Category
                [ColStart + 2] = 50, // Description
                [ColStart + 3] = 16, // Amount
            });
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

            row += 1;

            // Fund Position
            row = WriteSectionHeader(ws, row, ColStart, colEnd, "Fund Position");
            row = WriteKvRow(ws, row, ColStart, "Opening Balance", data.FundPosition.OpeningBalance, isAmount: true);
            row = WriteKvRow(ws, row, ColStart, "Total Billed", data.FundPosition.TotalBilled, isAmount: true);
            row = WriteKvRow(ws, row, ColStart, "Total Collected", data.FundPosition.TotalCollected, isAmount: true, valueColor: ColourPaidText);
            row = WriteKvRow(ws, row, ColStart, "Total Expenses", data.FundPosition.TotalExpenses, isAmount: true);
            row = WriteKvRow(ws, row, ColStart, "Closing Balance", data.FundPosition.ClosingBalance, isAmount: true, bold: true, highlight: true);
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
            ApplyPreferredColumnWidths(ws, new Dictionary<int, double>
            {
                [ColStart + 0] = 34, // Label
                [ColStart + 1] = 20, // Value
            });
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

            var monthlySummary = data.MonthSummary ?? new List<MonthSummaryDto>();
            if (monthlySummary.Count == 0)
            {
                row = WriteEmptyTableMessage(ws, row, ColStart, colEnd, "No monthly summary data for this year.");
            }
            else
            {
                int dataStart = row;
                foreach (var m in monthlySummary)
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
            }
            FinalizeSheet(ws, ColStart, colEnd);
            ApplyPreferredColumnWidths(ws, new Dictionary<int, double>
            {
                [ColStart + 0] = 20, // Month
                [ColStart + 1] = 14, // Billed
                [ColStart + 2] = 14, // Collected
                [ColStart + 3] = 14, // Expenses
                [ColStart + 4] = 14, // Net
                [ColStart + 5] = 12, // Status
            });
        }

        private static void BuildYearlyExpensesSheet(XLWorkbook wb, YearlyReportDto data)
        {
            // Data cols: C(3)=Date D(4)=Category E(5)=Description F(6)=Amount; title/headers span C:H for visual balance
            const int colEnd = ColStart + 5; // column H (used for title & section headers)
            const int dataColEnd = ColStart + 3; // column F (used for data rows & total)
            var ws = wb.AddWorksheet("Expenses");
            SetSheetDefaults(ws);
            int row = 1;

            row = WriteReportTitle(ws, row, ColStart, colEnd, data.SocietyName, $"Expenses - {data.YearLabel}");

            int headerRow = row;
            WriteTableHeader(ws, row, ColStart, new[] { "Date", "Category", "Description", "Amount" });
            ws.SheetView.FreezeRows(row); // freeze through table header row
            row++;

            var expenses = (data.Expenses ?? new List<ExpenseDto>())
                .OrderBy(e => e.DateIncurred)
                .ThenBy(e => e.CategoryName)
                .ToList();

            if (expenses.Count == 0)
            {
                row = WriteEmptyTableMessage(ws, row, ColStart, dataColEnd, "No expense entries recorded for this year.");
            }
            else
            {
                int dataStart = row;
                foreach (var exp in expenses)
                {
                    bool isAlternate = (row - dataStart) % 2 == 1; // Alternate every other row

                    ws.Cell(row, ColStart).Value = exp.DateIncurred.ToDateTime(TimeOnly.MinValue);
                    ws.Cell(row, ColStart).Style.DateFormat.Format = "dd-mmm-yyyy";
                    ws.Cell(row, ColStart + 1).Value = exp.CategoryName;
                    ws.Cell(row, ColStart + 2).Value = string.IsNullOrWhiteSpace(exp.Description) ? "-" : exp.Description;
                    ws.Cell(row, ColStart + 2).Style.Alignment.WrapText = true;
                    ws.Cell(row, ColStart + 2).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                    ws.Cell(row, ColStart + 3).Value = exp.TotalAmount;
                    ws.Cell(row, ColStart + 3).Style.NumberFormat.Format = AmountFormat;
                    ws.Cell(row, ColStart + 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
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
                        [ColStart + 3] = $"SUM({ws.Cell(dataStart, ColStart + 3).Address}:{ws.Cell(dataEnd, ColStart + 3).Address})",
                    });
                    // Overwrite formula with computed numeric value so the amount always shows
                    ws.Cell(row, ColStart + 3).Value = total;
                    ws.Cell(row, ColStart + 3).Style.NumberFormat.Format = AmountFormat;
                    ws.Cell(row, ColStart + 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    row++;
                }

                ws.Range(headerRow, ColStart, row - 1, dataColEnd).SetAutoFilter();
            }

            FinalizeSheet(ws, ColStart, colEnd);
            ApplyPreferredColumnWidths(ws, new Dictionary<int, double>
            {
                [ColStart + 0] = 14, // Date
                [ColStart + 1] = 22, // Category
                [ColStart + 2] = 50, // Description
                [ColStart + 3] = 16, // Amount
            });
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
            var displaySocietyName = string.IsNullOrWhiteSpace(societyName)
                ? "Society"
                : $"{BuildingIcon} {societyName.Trim()}";

            // Row 1: Society Name — merged C:colEnd
            ws.Range(row, colStart, row, colEnd).Merge();
            var titleCell = ws.Cell(row, colStart);
            titleCell.Value = displaySocietyName;
            titleCell.Style.Font.FontSize = TitleFontSize;
            titleCell.Style.Font.Bold = true;
            titleCell.Style.Font.FontColor = XLColor.White;
            titleCell.Style.Fill.BackgroundColor = ColourHeaderBg;
            titleCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            titleCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            titleCell.Style.Alignment.WrapText = true;
            titleCell.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            titleCell.Style.Border.OutsideBorderColor = ColourBorderDark;
            ws.Row(row).Height = GetWrappedRowHeight(displaySocietyName, colEnd - colStart + 1, 38);
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
            subCell.Style.Alignment.WrapText = true;
            subCell.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            subCell.Style.Border.OutsideBorderColor = ColourBorderDark;
            ws.Row(row).Height = GetWrappedRowHeight(subtitle, colEnd - colStart + 1, 28);
            row += 2; // blank spacing after title

            return row;
        }

        private static int WriteReportMetadata(IXLWorksheet ws, int row, int colStart, int colEnd, string societyName, string periodLabel, string reportType)
        {
            var metadataText = $"{reportType} • {societyName} • {periodLabel} • Exported: {DateTime.Now:dd MMM yyyy, HH:mm}";
            ws.Range(row, colStart, row, colEnd).Merge();
            var cell = ws.Cell(row, colStart);
            cell.Value = metadataText;
            cell.Style.Font.FontSize = 9;
            cell.Style.Font.Italic = true;
            cell.Style.Font.FontColor = XLColor.DarkGray;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F9FAFB");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            cell.Style.Alignment.WrapText = true;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = ColourBorder;
            ws.Row(row).Height = 26;
            return row + 1;
        }

        private static int WriteEmptyTableMessage(IXLWorksheet ws, int row, int colStart, int colEnd, string message)
        {
            ws.Range(row, colStart, row, colEnd).Merge();
            var cell = ws.Cell(row, colStart);
            cell.Value = message;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = ColourPendingText;
            cell.Style.Fill.BackgroundColor = ColourPendingBg;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            cell.Style.Alignment.WrapText = true;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = ColourBorder;
            ws.Row(row).Height = GetWrappedRowHeight(message, colEnd - colStart + 1, 26);
            return row + 1;
        }

        private static double GetWrappedRowHeight(string text, int mergedColumnCount, double minimumHeight = 24)
        {
            if (string.IsNullOrWhiteSpace(text))
                return minimumHeight;

            var columnWidthFactor = Math.Max(16, mergedColumnCount * 10.5);
            var estimatedLines = Math.Max(1, (int)Math.Ceiling(text.Length / (double)columnWidthFactor));
            return Math.Max(minimumHeight, estimatedLines * 15.5 + 8);
        }

        private static void WriteReportFooter(IXLWorksheet ws, int colStart, int colEnd, string note)
        {
            var lastRow = ws.LastRowUsed();
            var footerRow = lastRow is null ? 1 : lastRow.RowNumber() + 1;
            ws.Range(footerRow, colStart, footerRow, colEnd).Merge();
            var cell = ws.Cell(footerRow, colStart);
            cell.Value = note;
            cell.Style.Font.FontSize = 9;
            cell.Style.Font.Italic = true;
            cell.Style.Font.FontColor = XLColor.DarkGray;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F9FAFB");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = ColourBorder;
            ws.Row(footerRow).Height = 18;
        }

        private static string GetResidentDisplayName(string? tenantName, string? ownerName)
        {
            if (!string.IsNullOrWhiteSpace(tenantName)) return tenantName.Trim();
            if (!string.IsNullOrWhiteSpace(ownerName)) return ownerName.Trim();
            return "-";
        }

        private static int WriteSectionHeader(IXLWorksheet ws, int row, int colStart, int colEnd, string title)
        {
            ws.Range(row, colStart, row, colEnd).Merge();
            var cell = ws.Cell(row, colStart);
            cell.Value = title;
            cell.Style.Font.FontSize = SectionSize;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = ColourWhite;
            cell.Style.Fill.BackgroundColor = ColourSectionBg;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.WrapText = true;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = ColourBorderDark;
            ws.Row(row).Height = GetWrappedRowHeight(title, colEnd - colStart + 1, 28);
            return row + 1;
        }

        private static int WriteKvRow(IXLWorksheet ws, int row, int colStart, string key, object value, bool isAmount = false, XLColor? valueColor = null, bool bold = false, bool highlight = false)
        {
            ws.Cell(row, colStart).Value = key;
            ws.Cell(row, colStart).Style.Font.Bold = true;
            ws.Cell(row, colStart).Style.Fill.BackgroundColor = highlight ? ColourHighlightBg : ColourLightBg;
            ws.Cell(row, colStart).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Cell(row, colStart).Style.Border.OutsideBorderColor = ColourBorder;

            var valCell = ws.Cell(row, colStart + 1);
            if (isAmount && value is decimal decimalValue)
            {
                valCell.Value = decimalValue;
                valCell.Style.NumberFormat.Format = AmountFormat;
            }
            else
            {
                valCell.Value = XLCellValue.FromObject(value);
            }

            valCell.Style.Font.Bold = bold;
            if (valueColor != null) valCell.Style.Font.FontColor = valueColor;
            valCell.Style.Fill.BackgroundColor = highlight ? ColourHighlightBg : ColourWhite;
            valCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            valCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            valCell.Style.Border.OutsideBorderColor = ColourBorder;

            return row + 1;
        }

        private static int WriteAlertRow(IXLWorksheet ws, int row, int colStart, int colEnd, string alert, bool isWarning)
        {
            ws.Range(row, colStart, row, colEnd).Merge();
            var cell = ws.Cell(row, colStart);
            cell.Value = alert;
            cell.Style.Font.FontColor = isWarning ? ColourPendingText : ColourPaidText;
            cell.Style.Fill.BackgroundColor = isWarning ? ColourPendingBg : ColourPaidBg;
            cell.Style.Font.Italic = true;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            cell.Style.Alignment.WrapText = true;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = ColourBorder;
            ws.Row(row).Height = GetWrappedRowHeight(alert, colEnd - colStart + 1, 26);
            return row + 1;
        }

        private static void WriteTableHeader(IXLWorksheet ws, int row, int colStart, string[] headers)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(row, colStart + i);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = ColourWhite;
                cell.Style.Fill.BackgroundColor = ColourSectionBg;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                cell.Style.Alignment.WrapText = true;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.OutsideBorderColor = ColourBorderDark;
            }

            ws.Row(row).Height = 24;
        }

        private static void ApplyRowBorder(IXLWorksheet ws, int row, int colStart, int colEnd, bool isAlternate)
        {
            var range = ws.Range(row, colStart, row, colEnd);
            range.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
            range.Style.Border.LeftBorderColor = ColourBorder;
            range.Style.Border.RightBorder = XLBorderStyleValues.Thin;
            range.Style.Border.RightBorderColor = ColourBorder;
            range.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            range.Style.Border.BottomBorderColor = ColourBorder;
            if (isAlternate)
            {
                range.Style.Fill.BackgroundColor = ColourAlternateRow;
            }
        }

        private static void ApplyTotalRow(IXLWorksheet ws, int row, int colStart, int colEnd, Dictionary<int, string> formulas)
        {
            var range = ws.Range(row, colStart, row, colEnd);
            range.Style.Font.Bold = true;
            range.Style.Fill.BackgroundColor = ColourTotalRowBg;
            range.Style.Border.TopBorder = XLBorderStyleValues.Medium;
            range.Style.Border.TopBorderColor = ColourBorderDark;
            range.Style.Border.BottomBorder = XLBorderStyleValues.Double;
            range.Style.Border.BottomBorderColor = ColourBorderDark;

            ws.Cell(row, colStart).Value = "Total";

            foreach (var kvp in formulas)
            {
                var cell = ws.Cell(row, kvp.Key);
                cell.FormulaA1 = kvp.Value;
                cell.Style.Font.Bold = true;
            }
        }

        private static void FinalizeSheet(IXLWorksheet ws, int colStart, int colEnd)
        {
            ws.Columns(colStart, colEnd).AdjustToContents();
            for (int i = colStart; i <= colEnd; i++)
            {
                var width = ws.Column(i).Width + 2.5;
                ws.Column(i).Width = Math.Min(140, Math.Max(12, width));
            }
        }

        private static void ApplyPreferredColumnWidths(IXLWorksheet ws, Dictionary<int, double> widths)
        {
            foreach (var (column, width) in widths)
            {
                ws.Column(column).Width = width;
            }
        }
    }
}
