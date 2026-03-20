using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using SocietyLedger.Api.Extensions;
using SocietyLedger.Api.Filters;
using SocietyLedger.Application.DTOs.Billing;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
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
                    Summary     = "Get billing status",
                    Description = "Returns whether bills have been generated for the current calendar month and how many were created."
                )]
                async (IBillingService billingService, HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();

                    if (userId == 0)
                    {
                        Log.Warning("Unauthorized billing status request - invalid user ID");
                        var errorResponse = ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, "Invalid or missing authentication token", ctx.TraceIdentifier);
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
            // Society admin manually triggers billing for their own society.
            // Defaults to the current UTC month when BillingMonth is omitted.
            // The Hangfire background job also calls GenerateBillsAsync per-society
            // internally, so logic is never duplicated.
            app.MapPost("/generate-monthly",
                [Authorize]
                [SwaggerOperation(
                    Summary     = "Generate monthly maintenance bills (society admin)",
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
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("GenerateMonthlyBills")
            .Produces<ApiResponse<GenerateBillsResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(409)
            .Produces<ErrorResponse>(500);

            // POST /billing/generate-for-flat
            // Generates a bill for a specific flat for the current month (idempotent).
            app.MapPost("/generate-for-flat",
                [Authorize]
                [SwaggerOperation(
                    Summary     = "Generate bill for a flat",
                    Description = "Generates a bill for a specific flat for the current month. Idempotent: does nothing if bill already exists."
                )]
                async ([FromBody] long flatId, IBillingService billingService) =>
                {
                    var billingMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                    await billingService.GenerateBillForFlatAsync(flatId, billingMonth);
                    return Results.Ok(ApiResponse<string>.Success(null, $"Bill generated for flat {flatId} for {billingMonth:yyyy-MM} (if not already present)."));
                })
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
