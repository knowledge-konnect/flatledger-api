using SocietyLedger.Api.Extensions;
using SocietyLedger.Application.DTOs.Flat;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Shared;

namespace SocietyLedger.Api.Filters
{
    /// <summary>
    /// Endpoint filter that blocks flat creation when the society's plan flat limit would be exceeded.
    /// Returns 403 Forbidden so the frontend can show an upgrade prompt rather than a generic error.
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

            var bulkRequest = ctx.Arguments.OfType<BulkCreateFlatsRequest>().FirstOrDefault();
            var countToAdd = bulkRequest?.Flats?.Count ?? 1;

            var (allowed, message) = await _subscriptionService.CanAddFlatsAsync(userId, countToAdd);

            if (!allowed)
            {
                var error = ErrorResponse.Create(
                    ErrorCodes.FORBIDDEN,
                    message ?? "Flat limit reached. Please upgrade your plan to add more flats.",
                    ctx.HttpContext.TraceIdentifier);
                return Results.Json(error, statusCode: 403);
            }

            return await next(ctx);
        }
    }
}
