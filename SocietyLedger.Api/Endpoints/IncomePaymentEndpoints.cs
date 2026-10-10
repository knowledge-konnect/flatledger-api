using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SocietyLedger.Api.Extensions;
using SocietyLedger.Application.DTOs.MaintenancePayment;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Shared;
using Swashbuckle.AspNetCore.Annotations;

namespace SocietyLedger.Api.Endpoints
{
    public static class IncomePaymentEndpoints
    {
        public static void MapIncomePaymentRoutes(this RouteGroupBuilder app, string groupName, ApiVersionSet versionSet)
        {
            var version_1_0 = new ApiVersion(ApiConstants.API_VERSION_1_0);

            app.MapGet("/",
                [Authorize("ActiveSubscription")]
            [SwaggerOperation(
                    Summary = "Get income payments",
                    Description = "Returns non-maintenance income rows from the existing payment ledger. This endpoint is intentionally separate from maintenance payments to preserve category-based accounting rules."
                )]
            async ([FromQuery] string? period, [FromQuery] int page, [FromQuery] int pageSize, [FromQuery] string? category,
                   IMaintenancePaymentService paymentService, HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();

                    if (!string.IsNullOrEmpty(period) && !ValidationPatterns.BillingPeriod.IsMatch(period))
                        return Results.BadRequest("Invalid period format. Use YYYY-MM");

                    var result = await paymentService.GetIncomePaymentsBySocietyAsync(
                        userId,
                        period,
                        page < 1 ? 1 : page,
                        Math.Min(pageSize < 1 ? 20 : pageSize, 100),
                        category);

                    return Results.Ok(ApiResponse<ListMaintenancePaymentsResponse>.Success(
                        new ListMaintenancePaymentsResponse(result.ToList()),
                        "Income payments retrieved successfully"));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("GetIncomePayments")
            .Produces<ApiResponse<ListMaintenancePaymentsResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(500);
        }
    }
}
