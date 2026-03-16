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
    public static class AdminPlanEndpoints
    {
        public static void MapAdminPlanRoutes(this RouteGroupBuilder app, string groupName, ApiVersionSet versionSet)
        {
            var version_1_0 = new ApiVersion(ApiConstants.API_VERSION_1_0);

            // GET /api/admin/plans
            app.MapGet("/", [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(Summary = "List plans", Description = "Paginated, filterable list of all plans.")]
                async ([FromQuery] int page, [FromQuery] int pageSize, [FromQuery] string? search, [FromQuery] bool? isActive, IAdminPlanService service) =>
                {
                    var result = await service.GetPlansAsync(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, search, isActive);
                    return Results.Ok(ApiResponse<PagedResult<AdminPlanDto>>.Success(result));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("AdminListPlans");

            // GET /api/admin/plans/{id}
            app.MapGet("/{id:guid}", [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(Summary = "Get plan details", Description = "Get details for a specific plan.")]
                async (Guid id, IAdminPlanService service) =>
                {
                    var plan = await service.GetPlanByIdAsync(id);
                    return plan == null ? Results.NotFound(ErrorResponse.Create("NOT_FOUND", "Plan not found"))
                                        : Results.Ok(ApiResponse<AdminPlanDto>.Success(plan));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("AdminGetPlan");

            // POST /api/admin/plans
            app.MapPost("/", [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(Summary = "Create plan", Description = "Create a new subscription plan.")]
                async ([FromBody] AdminPlanCreateRequest req, IAdminPlanService service) =>
                {
                    var plan = await service.CreatePlanAsync(req);
                    return Results.Ok(ApiResponse<AdminPlanDto>.Success(plan, "Plan created successfully"));
                })
            .AddEndpointFilter<FluentValidationFilter<AdminPlanCreateRequest>>()
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("AdminCreatePlan");

            // PUT /api/admin/plans/{id}
            app.MapPut("/{id:guid}", [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(Summary = "Update plan", Description = "Update an existing plan.")]
                async (Guid id, [FromBody] AdminPlanUpdateRequest req, IAdminPlanService service) =>
                {
                    var plan = await service.UpdatePlanAsync(id, req);
                    return Results.Ok(ApiResponse<AdminPlanDto>.Success(plan, "Plan updated successfully"));
                })
            .AddEndpointFilter<FluentValidationFilter<AdminPlanUpdateRequest>>()
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("AdminUpdatePlan");

            // DELETE /api/admin/plans/{id}
            app.MapDelete("/{id:guid}", [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(Summary = "Delete plan", Description = "Delete a plan.")]
                async (Guid id, IAdminPlanService service) =>
                {
                    await service.DeletePlanAsync(id);
                    return Results.Ok(ApiResponse<string>.Success("Plan deleted successfully"));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("AdminDeletePlan");
        }
    }
}
