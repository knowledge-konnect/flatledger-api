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
    public static class AdminFeatureFlagEndpoints
    {
        public static void MapAdminFeatureFlagRoutes(this RouteGroupBuilder app, string groupName, ApiVersionSet versionSet)
        {
            var v1 = new ApiVersion(ApiConstants.API_VERSION_1_0);

            // GET /api/admin/features
            app.MapGet("/", [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(Summary = "List feature flags", Description = "Paginated list of feature flags with optional filters.")]
                async ([FromQuery] int page, [FromQuery] int pageSize, [FromQuery] string? search, [FromQuery] long? societyId,
                       IAdminFeatureFlagService service) =>
                {
                    var result = await service.GetFlagsAsync(page < 1 ? 1 : page, pageSize < 1 ? 50 : pageSize, search, societyId);
                    return Results.Ok(ApiResponse<PagedResult<FeatureFlagDto>>.Success(result));
                })
            .WithTags(groupName).WithApiVersionSet(versionSet).HasApiVersion(v1).WithName("AdminListFeatureFlags");

            // GET /api/admin/features/{id}
            app.MapGet("/{id:long}", [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(Summary = "Get feature flag details")]
                async (long id, IAdminFeatureFlagService service) =>
                {
                    var flag = await service.GetFlagByIdAsync(id);
                    return flag == null ? Results.NotFound(ErrorResponse.Create("NOT_FOUND", "Feature flag not found"))
                                       : Results.Ok(ApiResponse<FeatureFlagDto>.Success(flag));
                })
            .WithTags(groupName).WithApiVersionSet(versionSet).HasApiVersion(v1).WithName("AdminGetFeatureFlag");

            // POST /api/admin/features
            app.MapPost("/", [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(Summary = "Create feature flag")]
                async ([FromBody] FeatureFlagCreateRequest req, IAdminFeatureFlagService service) =>
                {
                    var flag = await service.CreateFlagAsync(req);
                    return Results.Ok(ApiResponse<FeatureFlagDto>.Success(flag, "Feature flag created successfully"));
                })
            .AddEndpointFilter<FluentValidationFilter<FeatureFlagCreateRequest>>()
            .WithTags(groupName).WithApiVersionSet(versionSet).HasApiVersion(v1).WithName("AdminCreateFeatureFlag");

            // PUT /api/admin/features/{id}
            app.MapPut("/{id:long}", [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(Summary = "Update feature flag")]
                async (long id, [FromBody] FeatureFlagUpdateRequest req, IAdminFeatureFlagService service) =>
                {
                    var flag = await service.UpdateFlagAsync(id, req);
                    return Results.Ok(ApiResponse<FeatureFlagDto>.Success(flag, "Feature flag updated successfully"));
                })
            .AddEndpointFilter<FluentValidationFilter<FeatureFlagUpdateRequest>>()
            .WithTags(groupName).WithApiVersionSet(versionSet).HasApiVersion(v1).WithName("AdminUpdateFeatureFlag");

            // DELETE /api/admin/features/{id}
            app.MapDelete("/{id:long}", [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(Summary = "Delete feature flag")]
                async (long id, IAdminFeatureFlagService service) =>
                {
                    await service.DeleteFlagAsync(id);
                    return Results.Ok(ApiResponse<string>.Success("Feature flag deleted successfully"));
                })
            .WithTags(groupName).WithApiVersionSet(versionSet).HasApiVersion(v1).WithName("AdminDeleteFeatureFlag");
        }
    }
}
