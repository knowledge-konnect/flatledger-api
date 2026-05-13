using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using SocietyLedger.Api.Extensions;
using SocietyLedger.Api.Filters;
using SocietyLedger.Application.DTOs.Auth;
using SocietyLedger.Application.DTOs.User;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Shared;
using SocietyLedger.Domain.Exceptions;
using Swashbuckle.AspNetCore.Annotations;
namespace SocietyLedger.Api.Endpoints
{
    public static class AuthRoutes
    {
        private const string RefreshTokenCookieName = "refreshToken";

        /// <summary>
        /// Cookie path scoped to auth routes only.
        /// Must match the full API prefix so the browser sends the cookie on every
        /// /api/auth/* request (e.g. /api/auth/refresh, /api/auth/revoke).
        /// </summary>
        private const string RefreshTokenCookiePath = "/api/auth";

        /// <summary>
        /// Sets the refresh token as an httpOnly cookie scoped to the auth path.
        /// Cookie is Secure in non-development environments.
        /// </summary>
        private static void SetRefreshTokenCookie(
            HttpContext ctx,
            string refreshToken,
            DateTime expiresAt,
            IWebHostEnvironment env)
        {
            ctx.Response.Cookies.Append(RefreshTokenCookieName, refreshToken, new CookieOptions
            {
                HttpOnly = true,
                // For cross-origin support, SameSite=None and Secure must be true
                Secure   = true,
                SameSite = SameSiteMode.None,
                Path     = RefreshTokenCookiePath,
                Expires  = new DateTimeOffset(DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc))
            });
        }

        /// <summary>
        /// Clears the refresh token cookie by overwriting it with an expired, empty value.
        /// Attributes must match <see cref="SetRefreshTokenCookie"/> (same Name + Path + SameSite + Secure)
        /// so the browser recognises it as the same cookie and evicts it.
        /// </summary>
        private static void ClearRefreshTokenCookie(HttpContext ctx, IWebHostEnvironment env)
        {
            ctx.Response.Cookies.Append(RefreshTokenCookieName, string.Empty, new CookieOptions
            {
                HttpOnly = true,
                Secure   = true,
                SameSite = SameSiteMode.None,
                Path     = RefreshTokenCookiePath,
                Expires  = DateTimeOffset.UnixEpoch,
                MaxAge   = TimeSpan.Zero
            });
        }

        /// <summary>
        /// Maps authentication routes: register, login, token refresh, revoke, change password, update profile, and get current user.
        /// </summary>
        public static void MapAuthRoutes(this RouteGroupBuilder app, string groupName, ApiVersionSet versionSet)
        {
            var version_1_0 = new ApiVersion(ApiConstants.API_VERSION_1_0);

            // Register
            app.MapPost("/register",
                [AllowAnonymous]
            [SwaggerOperation(
                    Summary = "Register user",
                    Description = "Creates a new user account in the system."
                )]
            async ([FromBody] RegisterRequest request, IAuthService authService, HttpContext ctx, IWebHostEnvironment env) =>
                {
                    var ip = ctx.GetClientIp();
                    var res = await authService.RegisterAsync(request, ip);
                    // Deliver the refresh token as an httpOnly cookie; [JsonIgnore] keeps it out of the response body.
                    SetRefreshTokenCookie(ctx, res.RefreshToken, res.RefreshTokenExpiresAt, env);
                    return Results.Created("/auth/user", ApiResponse<RegisterResponse>.Success(res, "Account created successfully"));
                })
            .AddEndpointFilter<FluentValidationFilter<RegisterRequest>>()
            .RequireRateLimiting("AuthPolicy")
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("Register")
            .Produces<ApiResponse<RegisterResponse>>(201)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(409)
            .Produces<ErrorResponse>(500);


            // Login
            app.MapPost("/login",
                [AllowAnonymous]
            [SwaggerOperation(
                    Summary = "Login user",
                    Description = "Authenticates a user and returns an access token. The refresh token is set as an httpOnly cookie."
                )]
            async ([FromBody] LoginRequest request, IAuthService authService, HttpContext ctx, IWebHostEnvironment env) =>
                {
                    var ip = ctx.GetClientIp();
                    var res = await authService.LoginAsync(request, ip);
                    SetRefreshTokenCookie(ctx, res.RefreshToken, res.RefreshTokenExpiresAt, env);
                    return Results.Ok(ApiResponse<LoginResponse>.Success(res, "Logged in successfully"));
                })
            .AddEndpointFilter<FluentValidationFilter<LoginRequest>>()
            .RequireRateLimiting("AuthPolicy")
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("Login")
            .Produces<ApiResponse<LoginResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(500);


            // Refresh Token
            app.MapPost("/refresh",
                [AllowAnonymous]
            [SwaggerOperation(
                    Summary = "Refresh token",
                    Description = "Rotates the refresh token (read from the httpOnly cookie) and returns a new access token."
                )]
            async (IAuthService authService, ITokenService tokenService, HttpContext ctx, IWebHostEnvironment env) =>
                {
                    var ip = ctx.GetClientIp();

                    // Check cookie presence safely (do not log token value).
                    var cookiePresent = ctx.Request.Cookies.TryGetValue(RefreshTokenCookieName, out var refreshToken);
                    Log.Debug("Refresh endpoint called. CookiePresent={Present} TraceId={TraceId} Ip={Ip}", cookiePresent, ctx.TraceIdentifier, ip);

                    if (!cookiePresent || string.IsNullOrWhiteSpace(refreshToken))
                    {
                        Log.Warning("Refresh token cookie missing. TraceId={TraceId} Ip={Ip}", ctx.TraceIdentifier, ip);
                        var errorResponse = ErrorResponse.Create(ErrorCodes.INVALID_REQUEST, ErrorMessages.INVALID_TOKEN, ctx.TraceIdentifier);
                        return Results.Json(errorResponse, statusCode: 401);
                    }

                    // Log the token hash (never the raw value) so operators can correlate
                    // refresh attempts with rows in the token store.
                    try
                    {
                        var hashed = tokenService.HashToken(refreshToken);
                        Log.Debug("Refresh token hash for lookup: {Hash} TraceId={TraceId}", hashed, ctx.TraceIdentifier);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to hash refresh token for debugging. TraceId={TraceId}", ctx.TraceIdentifier);
                    }

                    try
                    {
                        var res = await authService.RefreshTokenAsync(refreshToken, ip);
                        // Rotate: overwrite the cookie with the newly issued refresh token.
                        SetRefreshTokenCookie(ctx, res.RefreshToken, res.RefreshTokenExpiresAt, env);
                        Log.Information("Refresh token rotated successfully. TraceId={TraceId} User={User}", ctx.TraceIdentifier, res.UserPublicId);
                        return Results.Ok(ApiResponse<LoginResponse>.Success(res, "Token refreshed successfully"));
                    }
                    catch (AuthenticationException ex)
                    {
                        Log.Warning(ex, "Refresh failed - invalid or expired token. TraceId={TraceId} Ip={Ip}", ctx.TraceIdentifier, ip);
                        var errorResponse = ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, ErrorMessages.INVALID_TOKEN, ctx.TraceIdentifier);
                        return Results.Json(errorResponse, statusCode: 401);
                    }
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("Refresh")
            .Produces<ApiResponse<LoginResponse>>(200)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(500);


            // Revoke Token (logout)
            app.MapPost("/revoke",
                [Authorize]
            [SwaggerOperation(
                    Summary = "Revoke refresh token",
                    Description = "Revokes the refresh token (read from the httpOnly cookie) and clears the cookie."
                )]
            async (IAuthService authService, HttpContext ctx, IWebHostEnvironment env) =>
                {
                    var ip = ctx.GetClientIp();

                    var refreshToken = ctx.Request.Cookies[RefreshTokenCookieName];
                    if (string.IsNullOrWhiteSpace(refreshToken))
                    {
                        var errorResponse = ErrorResponse.Create(ErrorCodes.INVALID_REQUEST, ErrorMessages.INVALID_REQUEST, ctx.TraceIdentifier);
                        return Results.Json(errorResponse, statusCode: 400);
                    }

                    await authService.RevokeRefreshTokenAsync(refreshToken, ip);
                    ClearRefreshTokenCookie(ctx, env);
                    return Results.Ok(ApiResponse<EmptyResponse>.Success(null, "Token revoked successfully"));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("Revoke")
            .Produces<ApiResponse<EmptyResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(404)
            .Produces<ErrorResponse>(500);

            // Change Password
            app.MapPost("/change-password",
                [Authorize]
            [SwaggerOperation(
                    Summary = "Change user password",
                    Description = "Allows an authenticated user to change their password. Requires verification of current password."
                )]
            async ([FromBody] ChangePasswordRequest request, IAuthService authService, HttpContext ctx) =>
                {
                    var userId = ctx.GetAuthenticatedUserId();
                    if (userId == 0)
                    {
                        Log.Warning("Unauthorized change password attempt - invalid user ID");
                        var errorResponse = ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, ErrorMessages.UNAUTHORIZED, ctx.TraceIdentifier);
                        return Results.Json(errorResponse, statusCode: 401);
                    }

                    var res = await authService.ChangePasswordAsync(userId, request);
                    Log.Information("Password changed successfully for user {UserId}", userId);
                    return Results.Ok(ApiResponse<ChangePasswordResponse>.Success(res, "Password changed successfully"));
                })
            .AddEndpointFilter<FluentValidationFilter<ChangePasswordRequest>>()
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("ChangePassword")
            .Produces<ApiResponse<ChangePasswordResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(404)
            .Produces<ErrorResponse>(500);

            // Update own profile (self-service)
            app.MapPatch("/profile",
                [Authorize]
                [SwaggerOperation(
                    Summary = "Update own profile",
                    Description = "Allows an authenticated user to update their own mobile number. Email and role changes are not permitted."
                )]
                async ([FromBody] UpdateProfileRequest request, IUserService userService, HttpContext ctx) =>
                {
                    var userId = ctx.GetAuthenticatedUserId();
                    if (userId == 0)
                    {
                        Log.Warning("Unauthorized profile update attempt - invalid user ID");
                        var errorResponse = ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, ErrorMessages.UNAUTHORIZED, ctx.TraceIdentifier);
                        return Results.Json(errorResponse, statusCode: 401);
                    }

                    var profile = await userService.UpdateProfileAsync(userId, request);
                    Log.Information("Profile updated for user {UserId}", userId);
                    return Results.Ok(ApiResponse<ProfileResponse>.Success(profile, "Profile updated successfully"));
                })
            .AddEndpointFilter<FluentValidationFilter<UpdateProfileRequest>>()
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("UpdateProfile")
            .Produces<ApiResponse<ProfileResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(500);

            app.MapGet("user",
    [SwaggerOperation(
        Summary = "Get current user",
        Description = "Returns the currently authenticated user's details."
    )]
            async (IUserService userService, HttpContext ctx) =>
    {
        var userId = ctx.GetAuthenticatedUserId();
        if (userId == 0)
        {
            var errorResponse = ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, ErrorMessages.UNAUTHORIZED, ctx.TraceIdentifier);
            return Results.Json(errorResponse, statusCode: 401);
        }

        var user = await userService.GetUserByIdAsync(userId);
        if (user == null)
        {
            var errorResponse = ErrorResponse.Create(ErrorCodes.RESOURCE_NOT_FOUND, ErrorMessages.RESOURCE_NOT_FOUND, ctx.TraceIdentifier);
            return Results.Json(errorResponse, statusCode: 404);
        }

        return Results.Ok(ApiResponse<UserResponseDto>.Success(user, "User profile retrieved successfully"));
    })
    .RequireAuthorization()
    .WithTags(groupName)
    .WithApiVersionSet(versionSet)
    .HasApiVersion(version_1_0)
    .WithName("GetCurrentUser")
    .Produces<ApiResponse<UserResponseDto>>(200)
    .Produces<ErrorResponse>(401)
    .Produces<ErrorResponse>(404)
    .Produces<ErrorResponse>(500);

            // Check email exists (step 1 of direct password reset)
            // POST /auth/check-email
            app.MapPost("/check-email",
                [AllowAnonymous]
                [SwaggerOperation(
                    Summary = "Check if email exists",
                    Description = "Returns whether an active account exists for the given email. " +
                                  "Used by the password reset flow to validate the email before showing the new-password form."
                )]
                async ([FromBody] ForgotPasswordRequest request, IAuthService authService, HttpContext ctx) =>
                {
                    var exists = await authService.CheckEmailExistsAsync(request.Email);
                    if (!exists)
                    {
                        var err = ErrorResponse.Create(
                            ErrorCodes.RESOURCE_NOT_FOUND,
                            "No active account found with this email address.",
                            ctx.TraceIdentifier);
                        return Results.Json(err, statusCode: 404);
                    }
                    return Results.Ok(ApiResponse<object>.Success(new { email = request.Email }, "Email verified"));
                })
            .AddEndpointFilter<FluentValidationFilter<ForgotPasswordRequest>>()
            .RequireRateLimiting("AuthPolicy")
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("CheckEmailExists")
            .Produces<ApiResponse<object>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(404)
            .Produces<ErrorResponse>(429)
            .Produces<ErrorResponse>(500);

            // Direct password reset (step 2 — no token or email required)
            // POST /auth/reset-password-direct
            app.MapPost("/reset-password-direct",
                [AllowAnonymous]
                [SwaggerOperation(
                    Summary = "Reset password directly",
                    Description = "Resets the password for the given email address. " +
                                  "Call POST /auth/check-email first to verify the email exists. " +
                                  "Returns an access token for auto-login on success."
                )]
                async ([FromBody] ResetPasswordDirectRequest request, IAuthService authService, HttpContext ctx) =>
                {
                    var ip = ctx.GetClientIp();
                    var result = await authService.ResetPasswordDirectAsync(request, ip);
                    Log.Information("Password reset directly for email {Email} from {IP}", request.Email, ip);
                    return Results.Ok(ApiResponse<PasswordResetResponse>.Success(result, "Password reset successfully."));
                })
            .AddEndpointFilter<FluentValidationFilter<ResetPasswordDirectRequest>>()
            .RequireRateLimiting("AuthPolicy")
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("ResetPasswordDirect")
            .Produces<ApiResponse<PasswordResetResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(404)
            .Produces<ErrorResponse>(429)
            .Produces<ErrorResponse>(500);
        }
    }
}
