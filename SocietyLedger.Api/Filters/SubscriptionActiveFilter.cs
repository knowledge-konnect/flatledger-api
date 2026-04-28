using SocietyLedger.Api.Extensions;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Shared;

namespace SocietyLedger.Api.Filters
{
    /// <summary>
    /// Endpoint filter that blocks write operations when the society has no active subscription.
    /// Apply to any POST/PUT/DELETE endpoint that must be gated on a valid subscription.
    /// Returns a consistent 400 ApiResponse on failure; continues the pipeline on success.
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
                return Results.BadRequest(ApiResponse<object>.Fail(message!));

            return await next(ctx);
        }
    }
}
