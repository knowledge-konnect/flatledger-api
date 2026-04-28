using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using SocietyLedger.Api.Extensions;
using SocietyLedger.Api.Filters;
using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Application.Interfaces.Services.Admin;
using SocietyLedger.Shared;
using Swashbuckle.AspNetCore.Annotations;
using System.IdentityModel.Tokens.Jwt;

namespace SocietyLedger.Api.Endpoints.Admin
{
    public static class AdminAuthEndpoints
    {
        /// <summary>
        /// Maps admin authentication routes: login and profile retrieval.
        /// All routes require the SuperAdmin policy except the login endpoint.
        /// </summary>
        public static void MapAdminAuthRoutes(
            this RouteGroupBuilder app,
            string groupName,
            ApiVersionSet versionSet)
        {
            var version_1_0 = new ApiVersion(ApiConstants.API_VERSION_1_0);

            // POST /api/admin/auth/login
            app.MapPost("/login",
                [AllowAnonymous]
                [SwaggerOperation(
                    Summary = "Admin login",
                    Description = "Authenticates the platform admin and returns a short-lived JWT (60 min). No refresh token — re-login is required on expiry."
                )]
                async ([FromBody] AdminLoginRequest request,
                       IAdminAuthService adminAuthService,
                       HttpContext ctx) =>
                {
                    var ip = ctx.GetClientIp();
                    var res = await adminAuthService.LoginAsync(request, ip);
                    Log.Information("Admin login successful for {Email}", request.Email);
                    return Results.Ok(ApiResponse<AdminLoginResponse>.Success(res, "Admin logged in successfully"));
                })
            .AddEndpointFilter<FluentValidationFilter<AdminLoginRequest>>()
            .RequireRateLimiting("AuthPolicy")
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("AdminLogin")
            .Produces<ApiResponse<AdminLoginResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(500);

            // GET /api/admin/auth/me
            app.MapGet("/me",
                [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(
                    Summary = "Admin profile",
                    Description = "Returns the authenticated platform admin's profile. Note: Admin tokens are valid for 60 minutes with no refresh. A 401 response means the session has expired and re-login via POST /api/admin/auth/login is required."
                )]
                async (HttpContext ctx, IAdminAuthService adminAuthService) =>
                {
                    // The sub claim holds the internal numeric admin ID, consistent with society auth.
                    var sub = ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                           ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                    if (!long.TryParse(sub, out var adminId))
                        return Results.Unauthorized();

                    var profile = await adminAuthService.GetProfileAsync(adminId);
                    return Results.Ok(ApiResponse<AdminProfileDto>.Success(profile));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("AdminMe")
            .RequireAuthorization("SuperAdmin")
            .Produces<ApiResponse<AdminProfileDto>>(200)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(404)
            .Produces<ErrorResponse>(500);
        }
    }
}
