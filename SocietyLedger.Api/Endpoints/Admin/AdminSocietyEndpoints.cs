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
    public static class AdminSocietyEndpoints
    {
        public static void MapAdminSocietyRoutes(this RouteGroupBuilder app, string groupName, ApiVersionSet versionSet)
        {
            var version_1_0 = new ApiVersion(ApiConstants.API_VERSION_1_0);

            // GET /api/admin/societies
            app.MapGet("/", [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(Summary = "List societies", Description = "Paginated, filterable list of all societies.")]
                async ([FromQuery] int page, [FromQuery] int pageSize, [FromQuery] string? search, [FromQuery] bool? isDeleted, IAdminSocietyService service) =>
                {
                    var result = await service.GetSocietiesAsync(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, search, isDeleted);
                    return Results.Ok(ApiResponse<PagedResult<AdminSocietyDto>>.Success(result));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("AdminListSocieties");

            // GET /api/admin/societies/{id}
            app.MapGet("/{id:long}", [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(Summary = "Get society details", Description = "Get details for a specific society.")]
                async (long id, IAdminSocietyService service) =>
                {
                    var society = await service.GetSocietyByIdAsync(id);
                    return society == null ? Results.NotFound(ErrorResponse.Create("NOT_FOUND", "Society not found"))
                                          : Results.Ok(ApiResponse<AdminSocietyDto>.Success(society));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("AdminGetSociety");

            // PUT /api/admin/societies/{id}
            app.MapPut("/{id:long}", [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(Summary = "Update society", Description = "Update an existing society.")]
                async (long id, [FromBody] AdminSocietyUpdateRequest req, IAdminSocietyService service) =>
                {
                    var society = await service.UpdateSocietyAsync(id, req);
                    return Results.Ok(ApiResponse<AdminSocietyDto>.Success(society, "Society updated successfully"));
                })
            .AddEndpointFilter<FluentValidationFilter<AdminSocietyUpdateRequest>>()
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("AdminUpdateSociety");
        }
    }
}
