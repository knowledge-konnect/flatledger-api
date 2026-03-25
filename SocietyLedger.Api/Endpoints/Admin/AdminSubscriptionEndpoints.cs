using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SocietyLedger.Api.Filters;
using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Application.Interfaces.Services.Admin;
using SocietyLedger.Shared;
using Swashbuckle.AspNetCore.Annotations;

namespace SocietyLedger.Api.Endpoints.Admin
{
    public static class AdminSubscriptionEndpoints
    {
        public static void MapAdminSubscriptionRoutes(this RouteGroupBuilder app, string groupName, ApiVersionSet versionSet)
        {
            var v1 = new ApiVersion(ApiConstants.API_VERSION_1_0);

            // GET /api/admin/subscriptions
            app.MapGet("/", [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(Summary = "List subscriptions", Description = "Paginated list of all subscriptions with optional filters.")]
                async ([FromQuery] int page, [FromQuery] int pageSize, [FromQuery] string? status, [FromQuery] long? userId,
                       IAdminSubscriptionService service) =>
                {
                    var result = await service.GetSubscriptionsAsync(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, status, userId);
                    return Results.Ok(ApiResponse<PagedResult<AdminSubscriptionDto>>.Success(result));
                })
            .WithTags(groupName).WithApiVersionSet(versionSet).HasApiVersion(v1).WithName("AdminListSubscriptions");

            // GET /api/admin/subscriptions/{id}
            app.MapGet("/{id:guid}", [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(Summary = "Get subscription details")]
                async (Guid id, IAdminSubscriptionService service) =>
                {
                    var sub = await service.GetSubscriptionByIdAsync(id);
                    return sub == null ? Results.NotFound(ErrorResponse.Create("NOT_FOUND", "Subscription not found"))
                                      : Results.Ok(ApiResponse<AdminSubscriptionDto>.Success(sub));
                })
            .WithTags(groupName).WithApiVersionSet(versionSet).HasApiVersion(v1).WithName("AdminGetSubscription");
        }
    }
}
