using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Application.Interfaces.Services.Admin;
using SocietyLedger.Shared;
using Swashbuckle.AspNetCore.Annotations;

namespace SocietyLedger.Api.Endpoints.Admin
{
    public static class AdminUserEndpoints
    {
        /// <summary>
        /// Maps admin user routes: paginated society user listing and individual user detail retrieval.
        /// Requires the SuperAdmin policy.
        /// </summary>
        public static void MapAdminUserRoutes(this RouteGroupBuilder app, string groupName, ApiVersionSet versionSet)
        {
            var v1 = new ApiVersion(ApiConstants.API_VERSION_1_0);

            // GET /api/admin/users
            app.MapGet("/", [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(Summary = "List users", Description = "Paginated list of society users with optional filters.")]
                async ([FromQuery] int page, [FromQuery] int pageSize, [FromQuery] long? societyId,
                       [FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] bool? isDeleted,
                       IAdminUserService service) =>
                {
                    var result = await service.GetUsersAsync(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize,
                                                             societyId, search, isActive, isDeleted);
                    return Results.Ok(ApiResponse<PagedResult<AdminUserDto>>.Success(result));
                })
            .WithTags(groupName).WithApiVersionSet(versionSet).HasApiVersion(v1).WithName("AdminListUsers");

            // GET /api/admin/users/{id}
            app.MapGet("/{id:long}", [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(Summary = "Get user details")]
                async (long id, IAdminUserService service) =>
                {
                    var user = await service.GetUserByIdAsync(id);
                    return user == null
                        ? Results.NotFound(ErrorResponse.Create("NOT_FOUND", "User not found"))
                        : Results.Ok(ApiResponse<AdminUserDto>.Success(user));
                })
            .WithTags(groupName).WithApiVersionSet(versionSet).HasApiVersion(v1).WithName("AdminGetUser");
        }
    }
}
