using SocietyLedger.Api.Extensions;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Shared;

namespace SocietyLedger.Api.Filters
{
    /// <summary>
    /// Endpoint filter that blocks write operations when the society has no active subscription.
    /// Returns 403 Forbidden with a structured ErrorResponse so the frontend can distinguish
    /// a subscription gate from a validation error (400).
    /// </summary>
    public class SubscriptionActiveFilter : IEndpointFilter
    {
        private readonly ISubscriptionService _subscriptionService;

        public SubscriptionActiveFilter(ISubscriptionService subscriptionService)
        {
            _subscriptionService = subscriptionService;
        }

        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
        {
            var userId = ctx.HttpContext.GetUserId();
            var (valid, message) = await _subscriptionService.ValidateSubscriptionAsync(userId);

            if (!valid)
            {
                var error = ErrorResponse.Create(
                    ErrorCodes.FORBIDDEN,
                    message ?? "Your subscription is inactive. Please renew to continue.",
                    ctx.HttpContext.TraceIdentifier);
                return Results.Json(error, statusCode: 403);
            }

            return await next(ctx);
        }
    }
}
