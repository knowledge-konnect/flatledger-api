using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using SocietyLedger.Api.Extensions;
using SocietyLedger.Application.DTOs.Dashboard;
using SocietyLedger.Infrastructure.Services;
using SocietyLedger.Shared;
using Swashbuckle.AspNetCore.Annotations;

namespace SocietyLedger.Api.Endpoints
{
    public static class DashboardEndpoints
    {
        /// <summary>
        /// Maps dashboard routes: aggregated financial summary for the authenticated user's society.
        /// </summary>
        public static void MapDashboardRoutes(this RouteGroupBuilder app, string groupName, ApiVersionSet versionSet)
        {
            var v1 = new ApiVersion(ApiConstants.API_VERSION_1_0);

            app.MapGet("/",
                [Authorize]
                [SwaggerOperation(
                    Summary = "Get dashboard",
                    Description = "Returns aggregated society financial summary for the authenticated user."
                )]
                async (
                    IDashboardService dashboardService,
                    HttpContext ctx,
                    [FromQuery] DateTime? startDate,
                    [FromQuery] DateTime? endDate,
                    CancellationToken cancellationToken) =>
                {
                    if (startDate.HasValue && endDate.HasValue && startDate > endDate)
                        return Results.BadRequest(ApiResponse<object>.Fail("Start date must be before end date"));

                    // Society ID resolution is handled inside the service using the userId claim.
                    var userId = ctx.GetUserId();
                    var data = await dashboardService.GetDashboardDataAsync(userId, startDate, endDate, cancellationToken);
                    return Results.Ok(ApiResponse<DashboardResponseDto>.Success(data, "Dashboard data retrieved successfully"));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(v1)
            .WithName("GetDashboard")
            .Produces<ApiResponse<DashboardResponseDto>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(500);
        }
    }
}
