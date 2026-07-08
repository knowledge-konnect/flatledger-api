using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using SocietyLedger.Api.Extensions;
using SocietyLedger.Api.Filters;
using SocietyLedger.Application.DTOs.MaintenanceConfig;
using SocietyLedger.Application.DTOs.Society;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Shared;
using Swashbuckle.AspNetCore.Annotations;

namespace SocietyLedger.Api.Endpoints
{
    public static class SocietyRoutes
    {
        /// <summary>
        /// Maps society routes: get own society, update society profile, and maintenance config.
        /// </summary>
        public static void MapSocietyRoutes(this RouteGroupBuilder app, string groupName, ApiVersionSet versionSet)
        {
            var version_1_0 = new ApiVersion(ApiConstants.API_VERSION_1_0);

            // GET /societies — returns the authenticated user's own society
            app.MapGet("/",
                [Authorize]
                [SwaggerOperation(
                    Summary = "Get own society",
                    Description = "Returns the society the authenticated user belongs to."
                )]
                async (ISocietyService societyService, HttpContext ctx) =>
                {
                    var userId = ctx.GetAuthenticatedUserId();
                    if (userId == 0)
                    {
                        var err = ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, ErrorMessages.UNAUTHORIZED, ctx.TraceIdentifier);
                        return Results.Json(err, statusCode: 401);
                    }
                    var society = await societyService.GetByUserAsync(userId);
                    return Results.Ok(ApiResponse<SocietyResponseDto>.Success(society, "Society retrieved successfully"));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("GetOwnSociety")
            .Produces<ApiResponse<SocietyResponseDto>>(200)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(404)
            .Produces<ErrorResponse>(500);

            // GET /societies/{publicId} — returns a society by public ID (must belong to caller)
            app.MapGet("/{publicId:guid}",
                [Authorize]
                [SwaggerOperation(
                    Summary = "Get society by ID",
                    Description = "Returns a society by its public ID. The caller must belong to that society."
                )]
                async (Guid publicId, ISocietyService societyService, HttpContext ctx) =>
                {
                    var userId = ctx.GetAuthenticatedUserId();
                    if (userId == 0)
                    {
                        var err = ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, ErrorMessages.UNAUTHORIZED, ctx.TraceIdentifier);
                        return Results.Json(err, statusCode: 401);
                    }
                    var society = await societyService.GetByPublicIdAsync(publicId, userId);
                    return Results.Ok(ApiResponse<SocietyResponseDto>.Success(society, "Society retrieved successfully"));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("GetSocietyById")
            .Produces<ApiResponse<SocietyResponseDto>>(200)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(403)
            .Produces<ErrorResponse>(404)
            .Produces<ErrorResponse>(500);

            // PUT /societies/{publicId} — update society profile (admin only)
            app.MapPut("/{publicId:guid}",
                [Authorize]
                [SwaggerOperation(
                    Summary = "Update society",
                    Description = "Updates the society's name and address details. Society Admin role required."
                )]
                async (
                    Guid publicId,
                    [FromBody] UpdateSocietyRequest request,
                    ISocietyService societyService,
                    HttpContext ctx) =>
                {
                    var userId = ctx.GetAuthenticatedUserId();
                    if (userId == 0)
                    {
                        var err = ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, ErrorMessages.UNAUTHORIZED, ctx.TraceIdentifier);
                        return Results.Json(err, statusCode: 401);
                    }
                    var society = await societyService.UpdateAsync(publicId, request, userId);
                    Log.Information("Society {PublicId} updated by user {UserId}", publicId, userId);
                    return Results.Ok(ApiResponse<SocietyResponseDto>.Success(society, "Society updated successfully"));
                })
            .AddEndpointFilter<SubscriptionActiveFilter>()
            .AddEndpointFilter<ViewerForbiddenFilter>()
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("UpdateSociety")
            .Produces<ApiResponse<SocietyResponseDto>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(403)
            .Produces<ErrorResponse>(404)
            .Produces<ErrorResponse>(500);

            // GET /societies/{societyPublicId}/maintenance-config
            app.MapGet("/{societyPublicId:guid}/maintenance-config",
                [Authorize]
                [SwaggerOperation(
                    Summary = "Get maintenance configuration",
                    Description = "Fetches the default maintenance billing configuration for a society. Admin or Treasurer role required."
                )]
                async (
                    Guid societyPublicId,
                    IMaintenanceConfigService configService,
                    HttpContext ctx) =>
                {
                    var userId = ctx.GetAuthenticatedUserId();
                    if (userId == 0)
                    {
                        Log.Warning("Unauthorized maintenance config get request - invalid user ID");
                        var err = ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, ErrorMessages.UNAUTHORIZED, ctx.TraceIdentifier);
                        return Results.Json(err, statusCode: 401);
                    }

                    var config = await configService.GetAsync(societyPublicId, userId);
                    Log.Information("Maintenance config retrieved for society {SocietyPublicId} by user {UserId}", societyPublicId, userId);
                    return Results.Ok(ApiResponse<MaintenanceConfigResponse>.Success(config, "Maintenance configuration retrieved"));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("GetMaintenanceConfig")
            .Produces<ApiResponse<MaintenanceConfigResponse>>(200)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(403)
            .Produces<ErrorResponse>(404)
            .Produces<ErrorResponse>(500);

            // PUT /societies/{societyPublicId}/maintenance-config
            app.MapPut("/{societyPublicId:guid}/maintenance-config",
                [Authorize]
                [SwaggerOperation(
                    Summary = "Save maintenance configuration",
                    Description = "Creates or updates the maintenance billing configuration for a society (upsert). Admin or Treasurer role required."
                )]
                async (
                    Guid societyPublicId,
                    [FromBody] SaveMaintenanceConfigRequest request,
                    IMaintenanceConfigService configService,
                    HttpContext ctx) =>
                {
                    var userId = ctx.GetAuthenticatedUserId();
                    if (userId == 0)
                    {
                        Log.Warning("Unauthorized maintenance config save request - invalid user ID");
                        var err = ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, ErrorMessages.UNAUTHORIZED, ctx.TraceIdentifier);
                        return Results.Json(err, statusCode: 401);
                    }

                    if (ctx.GetUserRoleCode() == RoleCodes.Viewer)
                        return Results.Json(ErrorResponse.Create(ErrorCodes.FORBIDDEN, "You do not have permission to perform this action.", ctx.TraceIdentifier), statusCode: 403);

                    var config = await configService.SaveAsync(societyPublicId, request, userId);
                    Log.Information("Maintenance config saved for society {SocietyPublicId} by user {UserId}", societyPublicId, userId);
                    return Results.Ok(ApiResponse<MaintenanceConfigResponse>.Success(config, "Maintenance configuration saved successfully"));
                })
            .AddEndpointFilter<FluentValidationFilter<SaveMaintenanceConfigRequest>>()
            .AddEndpointFilter<SubscriptionActiveFilter>()
            .AddEndpointFilter<ViewerForbiddenFilter>()
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("SaveMaintenanceConfig")
            .Produces<ApiResponse<MaintenanceConfigResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(403)
            .Produces<ErrorResponse>(404)
            .Produces<ErrorResponse>(500);
        }
    }
}
