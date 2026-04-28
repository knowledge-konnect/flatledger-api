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
                    return Results.Ok(ApiResponse<RegisterResponse>.Success(res, "Account created successfully"));
                })
            .AddEndpointFilter<FluentValidationFilter<RegisterRequest>>()
            .RequireRateLimiting("AuthPolicy")
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("Register")
            .Produces<ApiResponse<RegisterResponse>>(200)
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
            var errorResponse = ErrorResponse.Create("UNAUTHORIZED", "User not authenticated", ctx.TraceIdentifier);
            return Results.Json(errorResponse, statusCode: 401);
        }

        var user = await userService.GetUserByIdAsync(userId);
        if (user == null)
        {
            var errorResponse = ErrorResponse.Create("USER_NOT_FOUND", "User not found", ctx.TraceIdentifier);
            return Results.Json(errorResponse, statusCode: 404);
        }

        return Results.Ok(ApiResponse<UserResponseDto>.Success(user));
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

            // Forgot Password
            app.MapPost("/forgot-password",
            [AllowAnonymous]
            [SwaggerOperation(
                Summary = "Initiate password reset",
                Description = "Sends a password reset email to the provided email address. Always returns 200 success (no account enumeration)."
            )]
            async ([FromBody] ForgotPasswordRequest request, IAuthService authService, HttpContext ctx) =>
            {
                var ip = ctx.GetClientIp();
                var traceId = ctx.TraceIdentifier;

                try
                {
                    // Build the reset link template: https://app/reset-password?token={0}
                    // The token will be URL-encoded by string.Format
                    var resetLinkTemplate = "https://app.example.com/reset-password?token={0}";

                    await authService.ForgotPasswordAsync(request.Email, resetLinkTemplate, ip);

                    Log.Information("Forgot password request for {Email} from {IP} TraceId={TraceId}", request.Email, ip, traceId);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error in forgot password for {Email} from {IP} TraceId={TraceId}", request.Email, ip, traceId);
                }

                // Always return 200 to avoid account enumeration
                return Results.Ok(new { ok = true, message = "If an account exists with this email, password reset instructions have been sent." });
            })
            .AddEndpointFilter<FluentValidationFilter<ForgotPasswordRequest>>()
            .RequireRateLimiting("AuthPolicy")
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("ForgotPassword")
            .Produces(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(500);

            // Validate Password Reset Token
            app.MapGet("/reset-password/validate",
            [AllowAnonymous]
            [SwaggerOperation(
                Summary = "Validate password reset token",
                Description = "Validates that a password reset token is valid, unexpired, and unused."
            )]
            async ([FromQuery] string token, IAuthService authService, HttpContext ctx) =>
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    var errorResponse = ErrorResponse.Create(ErrorCodes.VALIDATION_FAILED, "Token is required", ctx.TraceIdentifier);
                    return Results.Json(errorResponse, statusCode: 400);
                }

                try
                {
                    await authService.ValidatePasswordResetTokenAsync(token);
                    return Results.Ok(new { ok = true });
                }
                catch (ValidationException)
                {
                    return Results.Json(
                        new { ok = false, code = "invalid_or_expired" },
                        statusCode: 400);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error validating password reset token. TraceId={TraceId}", ctx.TraceIdentifier);
                    var errorResponse = ErrorResponse.Create(ErrorCodes.INTERNAL_SERVER_ERROR, ErrorMessages.INTERNAL_SERVER_ERROR, ctx.TraceIdentifier);
                    return Results.Json(errorResponse, statusCode: 500);
                }
            })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("ValidateResetToken")
            .Produces(200)
            .Produces(400)
            .Produces<ErrorResponse>(500);

            // Reset Password
            app.MapPost("/reset-password",
            [AllowAnonymous]
            [SwaggerOperation(
                Summary = "Reset password with token",
                Description = "Resets the password using a valid password reset token. Token is single-use and becomes invalid after reset."
            )]
            async ([FromBody] ResetPasswordRequest request, IAuthService authService, HttpContext ctx) =>
            {
                var ip = ctx.GetClientIp();
                var traceId = ctx.TraceIdentifier;

                try
                {
                    var res = await authService.ResetPasswordAsync(request.Token, request.NewPassword, ip);
                    Log.Information("Password reset successfully from {IP} TraceId={TraceId}", ip, traceId);
                    return Results.Ok(res);
                }
                catch (ValidationException ex)
                {
                    Log.Warning("Password reset validation error from {IP} TraceId={TraceId}: {Message}", ip, traceId, ex.Message);
                    return Results.Json(
                        new { ok = false, code = "invalid_or_expired", message = ex.Message },
                        statusCode: 400);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error resetting password from {IP} TraceId={TraceId}", ip, traceId);
                    var errorResponse = ErrorResponse.Create(ErrorCodes.INTERNAL_SERVER_ERROR, ErrorMessages.INTERNAL_SERVER_ERROR, ctx.TraceIdentifier);
                    return Results.Json(errorResponse, statusCode: 500);
                }
            })
            .AddEndpointFilter<FluentValidationFilter<ResetPasswordRequest>>()
            .RequireRateLimiting("AuthPolicy")
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("ResetPassword")
            .Produces(200)
            .Produces(400)
            .Produces<ErrorResponse>(429)
            .Produces<ErrorResponse>(500);
        }
    }
}
