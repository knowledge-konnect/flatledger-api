using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using SocietyLedger.Api.Extensions;
using SocietyLedger.Api.Filters;
using SocietyLedger.Application.DTOs.Billing;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Shared;
using Swashbuckle.AspNetCore.Annotations;

namespace SocietyLedger.Api.Endpoints
{
    public static class BillingEndpoints
    {
        /// <summary>
        /// Maps billing routes: get billing status and manually trigger monthly bill generation.
        /// </summary>
        public static void MapBillingRoutes(this RouteGroupBuilder app, string groupName, ApiVersionSet versionSet)
        {
            var version_1_0 = new ApiVersion(ApiConstants.API_VERSION_1_0);

            // GET /billing/status
            app.MapGet("/status",
                [Authorize]
            [SwaggerOperation(
                    Summary = "Get billing status",
                    Description = "Returns whether bills have been generated for the current calendar month and how many were created."
                )]
            async (IBillingService billingService, HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();

                    if (userId == 0)
                    {
                        Log.Warning("Unauthorized billing status request - invalid user ID");
                        var errorResponse = ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, ErrorMessages.UNAUTHORIZED, ctx.TraceIdentifier);
                        return Results.Json(errorResponse, statusCode: 401);
                    }

                    var result = await billingService.GetBillingStatusAsync(userId);
                    return Results.Ok(ApiResponse<BillingStatusResponse>.Success(result, "Billing status retrieved successfully"));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("GetBillingStatus")
            .Produces<ApiResponse<BillingStatusResponse>>(200)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(500);

            // POST /billing/generate-monthly
            app.MapPost("/generate-monthly",
                [Authorize]
            [SwaggerOperation(
                    Summary = "Generate monthly maintenance bills (society admin)",
                    Description = "Generates maintenance bills for all active flats in the calling user's society " +
                                  "for the specified month (YYYY-MM). Defaults to current UTC month when omitted. " +
                                  "Returns 409 if bills have already been generated for that period."
                )]
            async ([FromBody] GenerateMonthlyBillsRequest request, IBillingService billingService, HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();

                    if (userId == 0)
                    {
                        Log.Warning("Unauthorized /billing/generate-monthly request - invalid user ID");
                        return Results.Json(
                            ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, "Invalid or missing authentication token", ctx.TraceIdentifier),
                            statusCode: 401);
                    }

                    if (ctx.GetUserRoleCode() == RoleCodes.Viewer)
                        return Results.Json(new { error = "Forbidden", message = "You do not have permission to perform this action." }, statusCode: 403);

                    var billingMonthDate = request.GetBillingMonthDate();
                    var period           = billingMonthDate.ToString("yyyy-MM");

                    Log.Information(
                        "Manual billing trigger. UserId={UserId}, Period={Period}, TraceId={TraceId}",
                        userId, period, ctx.TraceIdentifier);

                    var result = await billingService.GenerateBillsAsync(userId, period);

                    Log.Information(
                        "Manual billing completed. UserId={UserId}, Period={Period}, BillsCreated={BillsCreated}",
                        userId, period, result.BillsCreated);

                    return Results.Ok(ApiResponse<GenerateBillsResponse>.Success(
                        result, $"Bills generated successfully for period {period}"));
                })
            .AddEndpointFilter<SubscriptionActiveFilter>()
            .AddEndpointFilter<ViewerForbiddenFilter>()
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("GenerateMonthlyBills")
            .Produces<ApiResponse<GenerateBillsResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(409)
            .Produces<ErrorResponse>(500);

            // POST /billing/trigger-monthly-job-now
            app.MapPost("/trigger-monthly-job-now",
                [Authorize("SuperAdmin")]
            [SwaggerOperation(
                    Summary = "Trigger monthly billing job now (SuperAdmin only)",
                    Description = "Runs the monthly billing background logic immediately for the current UTC month across societies. " +
                                  "Intended for verification/recovery; safe to re-run because existing bills are skipped. SuperAdmin access required."
                )]
            async (IBillingService billingService, HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();
                    if (userId == 0)
                        return Results.Json(
                            ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, "Invalid or missing authentication token", ctx.TraceIdentifier),
                            statusCode: 401);

                    if (ctx.GetUserRoleCode() == RoleCodes.Viewer)
                        return Results.Json(
                            new { error = "Forbidden", message = "You do not have permission to perform this action." },
                            statusCode: 403);

                    var billingMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                    var result = await billingService.GenerateMonthlyBillsAsync(billingMonth);

                    return Results.Ok(ApiResponse<BillingResult>.Success(
                        result,
                        $"Monthly billing job triggered for {billingMonth:yyyy-MM}. Created={result.BillsCreated}, Skipped={result.BillsSkipped}."));
                })
            .RequireRateLimiting("AuthPolicy")
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("TriggerMonthlyBillingJobNow")
            .Produces<ApiResponse<BillingResult>>(200)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(500);

            // POST /billing/catchup
            app.MapPost("/catchup",
                [Authorize("SuperAdmin")]
                [SwaggerOperation(
                    Summary     = "Trigger catch-up billing for a past period (SuperAdmin only)",
                    Description = "Generates bills for all active societies for the specified past period. " +
                                  "Defaults to the previous calendar month when period is omitted. " +
                                  "Returns 400 if the period is in the future or more than 12 months in the past."
                )]
                async ([FromBody] CatchupBillingRequest request, IBillingService billingService, HttpContext ctx) =>
                {
                    var billingMonth = request.GetBillingMonthDate();
                    var period = billingMonth.ToString("yyyy-MM");
                    var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

                    if (billingMonth >= currentMonth)
                    {
                        var err = ErrorResponse.Create("PERIOD_IN_FUTURE",
                            $"Period '{period}' is in the future or the current month. Catch-up billing can only target past periods.",
                            ctx.TraceIdentifier);
                        return Results.Json(err, statusCode: 400);
                    }

                    if (billingMonth < currentMonth.AddMonths(-12))
                    {
                        var err = ErrorResponse.Create("PERIOD_TOO_OLD",
                            $"Period '{period}' is more than 12 months in the past. Maximum lookback is 12 months.",
                            ctx.TraceIdentifier);
                        return Results.Json(err, statusCode: 400);
                    }

                    var result = await billingService.GenerateMonthlyBillsAsync(billingMonth, source: "catchup-manual");
                    return Results.Ok(ApiResponse<BillingResult>.Success(
                        result, $"Catch-up billing completed for {period}."));
                })
            .RequireRateLimiting("AuthPolicy")
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("TriggerCatchupBilling")
            .Produces<ApiResponse<BillingResult>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(403)
            .Produces<ErrorResponse>(500);

            // POST /billing/generate-for-flat
            // Generates a bill for a specific flat for the current month (idempotent).
            app.MapPost("/generate-for-flat",
                [Authorize]
            [SwaggerOperation(
                    Summary = "Generate bill for a flat",
                    Description = "Generates a bill for a specific flat in a society for the current month. Idempotent: does nothing if bill already exists."
                )]
            async ([FromBody] GenerateBillForFlatRequest request, IBillingService billingService, HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();
                    var billingMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    await billingService.GenerateBillForFlatAsync(request.FlatPublicId, userId, billingMonth);
                    return Results.Ok(ApiResponse<string>.Success(null, $"Bill generated for flat {request.FlatPublicId} for {billingMonth:yyyy-MM} (if not already present)."));
                })
            .AddEndpointFilter<SubscriptionActiveFilter>()
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("GenerateBillForFlat")
            .Produces<ApiResponse<string>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(500);
        }
    }
}
