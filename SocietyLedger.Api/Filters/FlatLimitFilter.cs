using SocietyLedger.Api.Extensions;
using SocietyLedger.Application.DTOs.Flat;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Shared;

namespace SocietyLedger.Api.Filters
{
    /// <summary>
    /// Endpoint filter that blocks flat creation when the society's plan flat limit would be exceeded.
    /// Also validates the subscription itself — a missing or expired subscription is rejected first.
    /// Apply to both single and bulk flat creation endpoints.
    /// For bulk requests, checks whether adding ALL requested flats would exceed the plan limit.
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

            // For bulk requests, count how many flats are being added so the limit check
            // accounts for the full batch, not just a single slot.
            var bulkRequest = ctx.Arguments.OfType<BulkCreateFlatsRequest>().FirstOrDefault();
            var countToAdd = bulkRequest?.Flats?.Count ?? 1;

            var (allowed, message) = await _subscriptionService.CanAddFlatsAsync(userId, countToAdd);

            if (!allowed)
                return Results.BadRequest(ApiResponse<object>.Fail(message!));

            return await next(ctx);
        }
    }
}
