using SocietyLedger.Api.Extensions;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Shared;

namespace SocietyLedger.Api.Filters
{
    /// <summary>
    /// Endpoint filter that blocks flat creation when the society's plan flat limit would be exceeded.
    /// Also validates the subscription itself — a missing or expired subscription is rejected first.
    /// Apply only to the POST flat creation endpoint.
    /// Returns a consistent 400 ApiResponse on failure; continues the pipeline on success.
    /// </summary>
    public class FlatLimitFilter : IEndpointFilter
    {
        private readonly ISubscriptionService _subscriptionService;

        public FlatLimitFilter(ISubscriptionService subscriptionService)
        {
            _subscriptionService = subscriptionService;
        }

        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
        {
            var userId = ctx.HttpContext.GetUserId();
            var (allowed, message) = await _subscriptionService.CanAddFlatAsync(userId);

            if (!allowed)
                return Results.BadRequest(ApiResponse<object>.Fail(message!));

            return await next(ctx);
        }
    }
}
