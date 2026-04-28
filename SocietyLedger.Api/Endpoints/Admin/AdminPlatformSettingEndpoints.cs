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
    public static class AdminPlatformSettingEndpoints
    {
        /// <summary>
        /// Maps admin platform setting routes: list, get, upsert, and delete key-value platform settings.
        /// Requires the SuperAdmin policy.
        /// </summary>
        public static void MapAdminPlatformSettingRoutes(this RouteGroupBuilder app, string groupName, ApiVersionSet versionSet)
        {
            var v1 = new ApiVersion(ApiConstants.API_VERSION_1_0);

            // GET /api/admin/settings
            app.MapGet("/", [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(Summary = "List platform settings")]
                async ([FromQuery] int page, [FromQuery] int pageSize, [FromQuery] string? search,
                       IAdminPlatformSettingService service) =>
                {
                    var result = await service.GetSettingsAsync(page < 1 ? 1 : page, pageSize < 1 ? 50 : pageSize, search);
                    return Results.Ok(ApiResponse<PagedResult<PlatformSettingDto>>.Success(result));
                })
            .WithTags(groupName).WithApiVersionSet(versionSet).HasApiVersion(v1).WithName("AdminListSettings");

            // GET /api/admin/settings/{key}
            app.MapGet("/{key}", [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(Summary = "Get setting by key")]
                async (string key, IAdminPlatformSettingService service) =>
                {
                    var setting = await service.GetSettingByKeyAsync(key);
                    return setting == null ? Results.NotFound(ErrorResponse.Create("NOT_FOUND", "Setting not found"))
                                          : Results.Ok(ApiResponse<PlatformSettingDto>.Success(setting));
                })
            .WithTags(groupName).WithApiVersionSet(versionSet).HasApiVersion(v1).WithName("AdminGetSetting");

            // PUT /api/admin/settings  (upsert — create or update by key)
            app.MapPut("/", [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(Summary = "Upsert platform setting", Description = "Create or update a setting by key.")]
                async ([FromBody] PlatformSettingUpsertRequest req, IAdminPlatformSettingService service) =>
                {
                    var setting = await service.UpsertSettingAsync(req);
                    return Results.Ok(ApiResponse<PlatformSettingDto>.Success(setting, "Setting saved successfully"));
                })
            .AddEndpointFilter<FluentValidationFilter<PlatformSettingUpsertRequest>>()
            .WithTags(groupName).WithApiVersionSet(versionSet).HasApiVersion(v1).WithName("AdminUpsertSetting");

            // DELETE /api/admin/settings/{key}
            app.MapDelete("/{key}", [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(Summary = "Delete setting by key")]
                async (string key, IAdminPlatformSettingService service) =>
                {
                    await service.DeleteSettingAsync(key);
                    return Results.Ok(ApiResponse<string>.Success("Setting deleted successfully"));
                })
            .WithTags(groupName).WithApiVersionSet(versionSet).HasApiVersion(v1).WithName("AdminDeleteSetting");
        }
    }
}
