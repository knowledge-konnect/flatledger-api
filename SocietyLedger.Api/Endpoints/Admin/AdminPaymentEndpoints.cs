using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Application.Interfaces.Services.Admin;
using SocietyLedger.Shared;
using Swashbuckle.AspNetCore.Annotations;

namespace SocietyLedger.Api.Endpoints.Admin
{
    public static class AdminPaymentEndpoints
    {
        public static void MapAdminPaymentRoutes(this RouteGroupBuilder app, string groupName, ApiVersionSet versionSet)
        {
            var v1 = new ApiVersion(ApiConstants.API_VERSION_1_0);

            // GET /api/admin/payments
            app.MapGet("/", [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(Summary = "List payments", Description = "Paginated list of payments with optional filters.")]
                async ([FromQuery] int page, [FromQuery] int pageSize, [FromQuery] long? societyId,
                       [FromQuery] string? paymentType, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
                       IAdminPaymentService service) =>
                {
                    var result = await service.GetPaymentsAsync(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, societyId, paymentType, from, to);
                    return Results.Ok(ApiResponse<PagedResult<AdminPaymentDto>>.Success(result));
                })
            .WithTags(groupName).WithApiVersionSet(versionSet).HasApiVersion(v1).WithName("AdminListPayments");

        }
    }
}
