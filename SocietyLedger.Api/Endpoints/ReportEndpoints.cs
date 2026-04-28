using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using SocietyLedger.Api.Extensions;
using SocietyLedger.Application.DTOs.Reports;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Shared;
using Swashbuckle.AspNetCore.Annotations;

namespace SocietyLedger.Api.Endpoints
{
    public static class ReportEndpoints
    {
        /// <summary>
        /// Maps report routes: collection summary, defaulters, income vs expense,
        /// fund ledger, payment register, and expense by category.
        /// </summary>
        public static void MapReportRoutes(this RouteGroupBuilder app, string groupName, ApiVersionSet versionSet)
        {
            var v1 = new ApiVersion(ApiConstants.API_VERSION_1_0);

            // Collection Summary
            app.MapGet("/collection-summary",
                [Authorize]
                [SwaggerOperation(
                    Summary = "Collection Summary",
                    Description = "Total billed vs collected vs outstanding per period. Optionally filter by period range (format: YYYY-MM)."
                )]
                async (
                    IReportService reportService,
                    HttpContext ctx,
                    [FromQuery] string? startPeriod,
                    [FromQuery] string? endPeriod,
                    CancellationToken ct) =>
                {
                    var userId = ctx.GetUserId();
                    if (userId == 0) return Results.Json(
                        ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, "Invalid token", ctx.TraceIdentifier), statusCode: 401);

                    var result = await reportService.GetCollectionSummaryAsync(userId, startPeriod, endPeriod, ct);
                    return Results.Ok(ApiResponse<CollectionSummaryDto>.Success(result, "Collection summary retrieved successfully"));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet).HasApiVersion(v1)
            .WithName("GetCollectionSummary")
            .Produces<ApiResponse<CollectionSummaryDto>>(200)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(500);

            // Defaulters Report
            app.MapGet("/defaulters",
                [Authorize]
                [SwaggerOperation(
                    Summary = "Defaulters Report",
                    Description = "Lists all flats with pending dues sorted by outstanding amount. Use minOutstanding to filter (default: 0)."
                )]
                async (
                    IReportService reportService,
                    HttpContext ctx,
                    [FromQuery] decimal minOutstanding = 0,
                    CancellationToken ct = default) =>
                {
                    var userId = ctx.GetUserId();
                    if (userId == 0) return Results.Json(
                        ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, "Invalid token", ctx.TraceIdentifier), statusCode: 401);

                    var result = await reportService.GetDefaultersReportAsync(userId, minOutstanding, ct);
                    return Results.Ok(ApiResponse<List<DefaulterDto>>.Success(result, "Defaulters report retrieved successfully"));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet).HasApiVersion(v1)
            .WithName("GetDefaultersReport")
            .Produces<ApiResponse<List<DefaulterDto>>>(200)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(500);

            // Income vs Expense
            app.MapGet("/income-vs-expense",
                [Authorize]
                [SwaggerOperation(
                    Summary = "Income vs Expense",
                    Description = "Monthly income (collections) vs expenses with net surplus/deficit. Filter by date range."
                )]
                async (
                    IReportService reportService,
                    HttpContext ctx,
                    [FromQuery] DateOnly? startDate,
                    [FromQuery] DateOnly? endDate,
                    CancellationToken ct) =>
                {
                    var userId = ctx.GetUserId();
                    if (userId == 0) return Results.Json(
                        ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, "Invalid token", ctx.TraceIdentifier), statusCode: 401);

                    var result = await reportService.GetIncomeVsExpenseAsync(userId, startDate, endDate, ct);
                    return Results.Ok(ApiResponse<IncomeVsExpenseDto>.Success(result, "Income vs expense report retrieved successfully"));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet).HasApiVersion(v1)
            .WithName("GetIncomeVsExpense")
            .Produces<ApiResponse<IncomeVsExpenseDto>>(200)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(500);

            // Fund Ledger
            app.MapGet("/fund-ledger",
                [Authorize]
                [SwaggerOperation(
                    Summary = "Society Fund Ledger",
                    Description = "Full transaction history of the society fund with running balance. Filter by date range."
                )]
                async (
                    IReportService reportService,
                    HttpContext ctx,
                    [FromQuery] DateOnly? startDate,
                    [FromQuery] DateOnly? endDate,
                    CancellationToken ct) =>
                {
                    var userId = ctx.GetUserId();
                    if (userId == 0) return Results.Json(
                        ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, "Invalid token", ctx.TraceIdentifier), statusCode: 401);

                    var result = await reportService.GetFundLedgerAsync(userId, startDate, endDate, ct);
                    return Results.Ok(ApiResponse<FundLedgerReportDto>.Success(result, "Fund ledger retrieved successfully"));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet).HasApiVersion(v1)
            .WithName("GetFundLedger")
            .Produces<ApiResponse<FundLedgerReportDto>>(200)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(500);

            // Payment Collection Register
            app.MapGet("/payment-register",
                [Authorize]
                [SwaggerOperation(
                    Summary = "Payment Collection Register",
                    Description = "Paginated list of all payments received — flat, owner, amount, mode and reference. " +
                                  "Filter by date range. Use 'page' and 'pageSize' for pagination (defaults: 1 / 50)."
                )]
                async (
                    IReportService reportService,
                    HttpContext ctx,
                    [FromQuery] DateOnly? startDate,
                    [FromQuery] DateOnly? endDate,
                    [FromQuery] int page     = 1,
                    [FromQuery] int pageSize = 50,
                    CancellationToken ct = default) =>
                {
                    var userId = ctx.GetUserId();
                    if (userId == 0) return Results.Json(
                        ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, "Invalid token", ctx.TraceIdentifier), statusCode: 401);

                    var result = await reportService.GetPaymentRegisterAsync(userId, startDate, endDate, page, pageSize, ct);
                    return Results.Ok(ApiResponse<PagedResult<PaymentRegisterDto>>.Success(result, "Payment register retrieved successfully"));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet).HasApiVersion(v1)
            .WithName("GetPaymentRegister")
            .Produces<ApiResponse<PagedResult<PaymentRegisterDto>>>(200)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(500);

            // Expense by Category
            app.MapGet("/expense-by-category",
                [Authorize]
                [SwaggerOperation(
                    Summary = "Expense by Category",
                    Description = "Total spending broken down by expense category with entry count and date range. Filter by date range."
                )]
                async (
                    IReportService reportService,
                    HttpContext ctx,
                    [FromQuery] DateOnly? startDate,
                    [FromQuery] DateOnly? endDate,
                    CancellationToken ct) =>
                {
                    var userId = ctx.GetUserId();
                    if (userId == 0) return Results.Json(
                        ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, "Invalid token", ctx.TraceIdentifier), statusCode: 401);

                    var result = await reportService.GetExpenseByCategoryAsync(userId, startDate, endDate, ct);
                    return Results.Ok(ApiResponse<ExpenseByCategoryDto>.Success(result, "Expense by category retrieved successfully"));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet).HasApiVersion(v1)
            .WithName("GetExpenseByCategory")
            .Produces<ApiResponse<ExpenseByCategoryDto>>(200)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(500);

            // Download Monthly Report
            app.MapGet("/download/monthly",
                [Authorize]
                [SwaggerOperation(
                    Summary = "Download Monthly Report",
                    Description = "Downloads an Excel report for the given month with fund position, flat payment status, and expenses by category.\n\nAll monetary balances are signed: Positive = member owes the society; Negative = society owes the member (advance)."
                )]
                async (
                    IReportService reportService,
                    HttpContext ctx,
                    [FromQuery] int year,
                    [FromQuery] int month,
                    CancellationToken ct) =>
                {
                    var userId = ctx.GetUserId();
                    if (userId == 0) return Results.Json(
                        ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, "Invalid token", ctx.TraceIdentifier), statusCode: 401);

                    if (month < 1 || month > 12) return Results.Json(
                        ErrorResponse.Create("INVALID_PARAM", "month must be between 1 and 12", ctx.TraceIdentifier), statusCode: 400);

                    if (year < 2000 || year > 2100) return Results.Json(
                        ErrorResponse.Create("INVALID_PARAM", "year must be between 2000 and 2100", ctx.TraceIdentifier), statusCode: 400);

                    var (bytes, fileName) = await reportService.DownloadMonthlyReportAsync(userId, year, month, ct);
                    return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet).HasApiVersion(v1)
            .WithName("DownloadMonthlyReport")
            .Produces<FileResult>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(500);

            // Download Yearly Report
            app.MapGet("/download/yearly",
                [Authorize]
                [SwaggerOperation(
                    Summary = "Download Yearly Report",
                    Description = "Downloads an Excel report for the given year with fund position, month-by-month breakdown, and expenses by category. " +
                                  "Use yearType=financial (Apr-Mar, default) or yearType=calendar (Jan-Dec)."
                )]
                async (
                    IReportService reportService,
                    HttpContext ctx,
                    [FromQuery] int? year,
                    [FromQuery] string yearType = "financial",
                    CancellationToken ct = default) =>
                {
                    var userId = ctx.GetUserId();
                    if (userId == 0) return Results.Json(
                        ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, "Invalid token", ctx.TraceIdentifier), statusCode: 401);

                    var selectedYear = year ?? DateTime.UtcNow.Year;
                    if (selectedYear < 2000 || selectedYear > 2100) return Results.Json(
                        ErrorResponse.Create("INVALID_PARAM", "year must be between 2000 and 2100", ctx.TraceIdentifier), statusCode: 400);

                    if (yearType != "calendar" && yearType != "financial") return Results.Json(
                        ErrorResponse.Create("INVALID_PARAM", "yearType must be 'calendar' or 'financial'", ctx.TraceIdentifier), statusCode: 400);

                    var (bytes, fileName) = await reportService.DownloadYearlyReportAsync(userId, selectedYear, yearType, ct);
                    return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet).HasApiVersion(v1)
            .WithName("DownloadYearlyReport")
            .Produces<FileResult>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(500);
        }
    }
}
