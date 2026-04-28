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
    public static class AdminInvoiceEndpoints
    {
        /// <summary>
        /// Maps admin invoice routes: paginated SaaS subscription invoice listing with optional filters.
        /// Requires the SuperAdmin policy.
        /// </summary>
        public static void MapAdminInvoiceRoutes(this RouteGroupBuilder app, string groupName, ApiVersionSet versionSet)
        {
            var v1 = new ApiVersion(ApiConstants.API_VERSION_1_0);

            // GET /api/admin/invoices
            app.MapGet("/", [Authorize(Policy = "SuperAdmin")]
                [SwaggerOperation(Summary = "List SaaS invoices", Description = "Paginated list of subscription invoices with optional filters.")]
                async ([FromQuery] int page, [FromQuery] int pageSize, [FromQuery] long? userId,
                       [FromQuery] string? status, [FromQuery] string? invoiceType,
                       [FromQuery] DateTime? from, [FromQuery] DateTime? to,
                       IAdminInvoiceService service) =>
                {
                    var result = await service.GetInvoicesAsync(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize,
                                                                userId, status, invoiceType, from, to);
                    return Results.Ok(ApiResponse<PagedResult<AdminInvoiceDto>>.Success(result));
                })
            .WithTags(groupName).WithApiVersionSet(versionSet).HasApiVersion(v1).WithName("AdminListInvoices");
        }
    }
}
