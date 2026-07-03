using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SocietyLedger.Api.Extensions;
using SocietyLedger.Api.Filters;
using SocietyLedger.Application.DTOs.Subscription;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Shared;
using Swashbuckle.AspNetCore.Annotations;

namespace SocietyLedger.Api.Endpoints
{
    public static class SubscriptionEndpoints
    {
        /// <summary>
        /// Maps subscription routes: create trial, get status, and upgrade to a paid plan.
        /// </summary>
        public static void MapSubscriptionRoutes(this RouteGroupBuilder app, string groupName, ApiVersionSet versionSet)
        {
            var version_1_0 = new ApiVersion(ApiConstants.API_VERSION_1_0);

            // Create trial subscription
            app.MapPost("/trial",
                [Authorize]
            [SwaggerOperation(
                    Summary = "Create trial subscription",
                    Description = "Creates a 30-day free trial for the authenticated user on first registration."
                )]
            async (ISubscriptionService subscriptionService, HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();
                    await subscriptionService.CreateTrialSubscriptionAsync(userId);
                    var status = await subscriptionService.GetSubscriptionStatusAsync(userId);
                    return Results.Ok(ApiResponse<SubscriptionStatusResponse>.Success(status, "Trial started"));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("CreateTrialSubscription")
            .Produces<ApiResponse<SubscriptionStatusResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(500);

            // Get subscription status
            app.MapGet("/status",
                [Authorize]
            [SwaggerOperation(
                    Summary = "Get subscription status",
                    Description = "Returns the current subscription status for the authenticated user."
                )]
            async (ISubscriptionService subscriptionService, HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();
                    var result = await subscriptionService.GetSubscriptionStatusAsync(userId);
                    return Results.Ok(ApiResponse<SubscriptionStatusResponse>.Success(result, "Subscription status retrieved successfully"));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("GetSubscriptionStatus")
            .Produces<ApiResponse<SubscriptionStatusResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(500);

            // Get current subscription (society-scoped, used by frontend SubscriptionManagement)
            // GET /subscriptions/current?societyId=<uuid>
            // societyId is accepted as a query param for forward-compatibility but is ignored —
            // society isolation is always enforced via the authenticated user's JWT claim.
            app.MapGet("/current",
                [Authorize]
            [SwaggerOperation(
                    Summary = "Get current subscription",
                    Description = "Returns the current subscription for the authenticated user's society. " +
                                  "The societyId query param is accepted for client compatibility but society " +
                                  "isolation is always enforced server-side via the JWT claim."
                )]
            async (ISubscriptionService subscriptionService, HttpContext ctx,
                   [FromQuery] string? societyId) =>
                {
                    var userId = ctx.GetUserId();
                    var result = await subscriptionService.GetSubscriptionStatusAsync(userId);
                    // Return null-equivalent when no subscription exists so the frontend
                    // can distinguish "no subscription" from an error.
                    if (result.Status == "none")
                        return Results.Ok(ApiResponse<SubscriptionStatusResponse>.Success(
                            new SubscriptionStatusResponse { Status = "none", AccessAllowed = false },
                            "No active subscription"));
                    return Results.Ok(ApiResponse<SubscriptionStatusResponse>.Success(result, "Current subscription retrieved successfully"));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("GetCurrentSubscription")
            .Produces<ApiResponse<SubscriptionStatusResponse>>(200)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(500);

            // Subscribe to a plan
            app.MapPost("/subscribe",
                [Authorize]
            [SwaggerOperation(
                    Summary = "Subscribe to a plan",
                    Description = "Creates a subscription and processes payment."
                )]
            async ([FromBody] SubscribeRequest request, ISubscriptionService subscriptionService, HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();
                    var result = await subscriptionService.SubscribeAsync(userId, request);
                    return Results.Created("/subscriptions/status", ApiResponse<SubscribeResponse>.Success(result, "Subscription created successfully"));
                })
            .AddEndpointFilter<FluentValidationFilter<SubscribeRequest>>()
            .AddEndpointFilter<ViewerForbiddenFilter>()
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("Subscribe")
            .Produces<ApiResponse<SubscribeResponse>>(201)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(500);

            // Cancel subscription
            app.MapPost("/cancel",
                [Authorize]
            [SwaggerOperation(
                    Summary = "Cancel subscription",
                    Description = "Cancels the user's active subscription."
                )]
            async ([FromBody] CancelSubscriptionRequest request, ISubscriptionService subscriptionService, HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();
                    if (ctx.GetUserRoleCode() == RoleCodes.Viewer)
                        return Results.Json(new { error = "Forbidden", message = "You do not have permission to perform this action." }, statusCode: 403);
                    await subscriptionService.CancelSubscriptionAsync(userId, request);
                    return Results.Ok(ApiResponse<EmptyResponse>.Success(null, "Subscription cancelled successfully"));
                })
            .AddEndpointFilter<FluentValidationFilter<CancelSubscriptionRequest>>()
            .AddEndpointFilter<ViewerForbiddenFilter>()
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("CancelSubscription")
            .Produces<ApiResponse<EmptyResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(500);
        }
    }
}