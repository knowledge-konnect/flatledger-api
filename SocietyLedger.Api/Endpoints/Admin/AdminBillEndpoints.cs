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
    public static class AdminBillEndpoints
    {
        public static void MapAdminBillRoutes(this RouteGroupBuilder app, string groupName, ApiVersionSet versionSet)
        {
            var v1 = new ApiVersion(ApiConstants.API_VERSION_1_0);

            // GET /api/admin/bills
            app.MapGet("/", [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(Summary = "List bills", Description = "Paginated cross-society bill list with optional filters.")]
                async ([FromQuery] int page, [FromQuery] int pageSize, [FromQuery] long? societyId,
                       [FromQuery] string? status, [FromQuery] string? period,
                       [FromQuery] DateTime? from, [FromQuery] DateTime? to,
                       IAdminBillService service) =>
                {
                    var result = await service.GetBillsAsync(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize,
                                                             societyId, status, period, from, to);
                    return Results.Ok(ApiResponse<PagedResult<AdminBillDto>>.Success(result));
                })
            .WithTags(groupName).WithApiVersionSet(versionSet).HasApiVersion(v1).WithName("AdminListBills");
        }
    }
}
