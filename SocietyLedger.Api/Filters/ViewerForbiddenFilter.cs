using SocietyLedger.Api.Extensions;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Shared;

namespace SocietyLedger.Api.Filters
{
    /// <summary>
    /// Endpoint filter that rejects requests from users with the Viewer role.
    /// Apply to any write endpoint (POST/PUT/DELETE) that Viewers must not access.
    /// Returns a consistent ErrorResponse (403) instead of an anonymous object.
    /// </summary>
    public class ViewerForbiddenFilter : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
        {
            if (ctx.HttpContext.GetUserRoleCode() == RoleCodes.Viewer)
            {
                var error = ErrorResponse.Create(
                    ErrorCodes.FORBIDDEN,
                    "You do not have permission to perform this action.",
                    ctx.HttpContext.TraceIdentifier);
                return Results.Json(error, statusCode: 403);
            }

            return await next(ctx);
        }
    }
}
