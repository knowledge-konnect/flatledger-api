using Asp.Versioning;
using Asp.Versioning.Builder;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using SocietyLedger.Api.Extensions;
using SocietyLedger.Api.Filters;
using SocietyLedger.Application.DTOs.Flat;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Entities;
using FlatEntity = SocietyLedger.Domain.Entities.Flat;
using SocietyLedger.Infrastructure.Services.Common;
using SocietyLedger.Shared;
using Swashbuckle.AspNetCore.Annotations;

namespace SocietyLedger.Api.Endpoints
{
    public static class FlatRoutes
    {
        /// <summary>
        /// Maps flat routes: create, list, retrieve, update, and delete flats within a society.
        /// Requires an active subscription for all operations.
        /// </summary>
        public static void MapFlatRoutes(this RouteGroupBuilder app, string groupName, ApiVersionSet versionSet)
        {
            var version_1_0 = new ApiVersion(ApiConstants.API_VERSION_1_0);

            app.MapPost("/", [Authorize("ActiveSubscription")]
            [SwaggerOperation(
                Summary = "Create Flat",
                Description = "Creates a new flat record in the system."
            )]
            async ([FromBody] CreateFlatDto request, [FromServices] IFlatService service, HttpContext ctx) =>
            {
                var userId = ctx.GetUserId();
                var result = await service.CreateAsync(request, userId);
                Log.Information("Flat created successfully for FlatNo {FlatNo}", request.FlatNo);
                return Results.Ok(ApiResponse<FlatResponseDto>.Success(result, "Flat created successfully"));
            })
            .AddEndpointFilter<FluentValidationFilter<CreateFlatDto>>()
            .AddEndpointFilter<FlatLimitFilter>()
            .AddEndpointFilter<ViewerForbiddenFilter>()
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .Produces<ApiResponse<FlatResponseDto>>(201)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(409)
            .Produces<ErrorResponse>(500);

            app.MapGet("/", [Authorize("ActiveSubscription")]
            [SwaggerOperation(
                Summary = "Get all Flats for Current Society",
                Description = "Fetches flats for the authenticated user's society. Supports optional pagination, search, and filtering. When no params are provided, returns all flats (backward compatible)."
            )]
            async (
                IFlatService service,
                HttpContext ctx,
                [FromQuery] string? search,
                [FromQuery] string? statusCode,
                [FromQuery] int? page,
                [FromQuery] int? size,
                [FromQuery] string? sortBy,
                [FromQuery] string? sortDir) =>
            {
                var userId = ctx.GetUserId();

                // Validate sortBy
                var allowedSortBy = new[] { "flatNo", "ownerName", "maintenanceAmount", "createdAt" };
                var resolvedSortBy = sortBy ?? "createdAt";
                if (!allowedSortBy.Contains(resolvedSortBy, StringComparer.OrdinalIgnoreCase))
                    return Results.BadRequest(ApiResponse<object>.Fail($"Invalid sortBy value '{resolvedSortBy}'. Allowed: {string.Join(", ", allowedSortBy)}"));

                var resolvedSortDir = sortDir ?? "asc";
                if (!new[] { "asc", "desc" }.Contains(resolvedSortDir, StringComparer.OrdinalIgnoreCase))
                    return Results.BadRequest(ApiResponse<object>.Fail("Invalid sortDir value. Allowed: asc, desc"));

                // If no pagination params provided, return the full unpaginated list
                // to preserve backward compatibility with existing clients.
                if (page == null && size == null && search == null && statusCode == null && sortBy == null)
                {
                    var flats = await service.GetBySocietyAsync(userId);
                    Log.Information("Fetched {Count} flats for user {UserId}", flats.Count(), userId);
                    return Results.Ok(ApiResponse<ListFlatsResponse>.Success(
                        new ListFlatsResponse { Flats = flats.ToList() },
                        "Flats retrieved successfully"));
                }

                var resolvedPage = (page ?? 1) < 1 ? 1 : (page ?? 1);
                var resolvedSize = Math.Min(size ?? 10, 100);

                if (resolvedPage < 1)
                    return Results.BadRequest(ApiResponse<object>.Fail("page must be >= 1"));
                if (resolvedSize <= 0)
                    return Results.BadRequest(ApiResponse<object>.Fail("size must be > 0"));

                var result = await service.GetPagedAsync(userId, search, statusCode, resolvedPage, resolvedSize, resolvedSortBy, resolvedSortDir);
                Log.Information("Fetched paginated flats page={Page} size={Size} for user {UserId}", resolvedPage, resolvedSize, userId);
                return Results.Ok(ApiResponse<PagedFlatsResponse>.Success(result, "Flats retrieved successfully"));
            })
    .WithName("GetFlatsBySocietyId")
    .WithTags(groupName)
    .WithApiVersionSet(versionSet)
    .HasApiVersion(version_1_0)
    .Produces<ApiResponse<ListFlatsResponse>>(200)
    .Produces<ApiResponse<PagedFlatsResponse>>(200)
    .Produces<ErrorResponse>(400)
    .Produces<ErrorResponse>(401)
    .Produces<ErrorResponse>(404)
    .Produces<ErrorResponse>(500);


            // Get Flat by PublicId
            app.MapGet("/{publicId:guid}", [Authorize("ActiveSubscription")]
            [SwaggerOperation(
                Summary = "Get Flat by ID",
                Description = "Fetches flat details by public ID."
            )]
            async (Guid publicId, [FromServices] IFlatService service, HttpContext ctx) =>
            {
                var userId = ctx.GetUserId();
                var flat = await service.GetByPublicIdAsync(publicId, userId);
                if (flat == null)
                {
                    Log.Warning("Flat not found for PublicId {PublicId}", publicId);
                    var errorResponse = ErrorResponse.Create(ErrorCodes.RESOURCE_NOT_FOUND, ErrorMessages.RESOURCE_NOT_FOUND, ctx.TraceIdentifier);
                    return Results.Json(errorResponse, statusCode: 404);
                }

                Log.Information("Flat fetched successfully for PublicId {PublicId}", publicId);
                return Results.Ok(ApiResponse<FlatResponseDto>.Success(flat, "Flat retrieved successfully"));
            })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .Produces<ApiResponse<FlatResponseDto>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(404)
            .Produces<ErrorResponse>(500);

            // Update Flat
            app.MapPut("/", [Authorize("ActiveSubscription")]
            [SwaggerOperation(
                Summary = "Update Flat",
                Description = "Updates an existing flat record."
            )]
            async ([FromBody] UpdateFlatDto request, [FromServices] IFlatService service, HttpContext ctx) =>
            {
                var userId = ctx.GetUserId();
                var result = await service.UpdateAsync(request, userId);
                if (result == null)
                {
                    Log.Warning("Flat not found for update {PublicId}", request.PublicId);
                    var errorResponse = ErrorResponse.Create(ErrorCodes.RESOURCE_NOT_FOUND, ErrorMessages.RESOURCE_NOT_FOUND, ctx.TraceIdentifier);
                    return Results.Json(errorResponse, statusCode: 404);
                }

                Log.Information("Flat updated successfully for PublicId {PublicId}", request.PublicId);
                return Results.Ok(ApiResponse<FlatResponseDto>.Success(result, "Flat updated successfully"));
            })
            .AddEndpointFilter<FluentValidationFilter<UpdateFlatDto>>()
            .AddEndpointFilter<SubscriptionActiveFilter>()
            .AddEndpointFilter<ViewerForbiddenFilter>()
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .Produces<ApiResponse<FlatResponseDto>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(404)
            .Produces<ErrorResponse>(409)
            .Produces<ErrorResponse>(500);

            // Delete Flat
            app.MapDelete("/{publicId:guid}", [Authorize("ActiveSubscription")]
            [SwaggerOperation(
                Summary = "Delete Flat",
                Description = "Deletes a flat record by public ID."
            )]
            async (Guid publicId, [FromServices] IFlatService service, HttpContext ctx) =>
            {
                var userId = ctx.GetUserId();
                var deleted = await service.DeleteByPublicIdAsync(publicId, userId);
                if (!deleted)
                {
                    Log.Warning("Flat not found for deletion {PublicId}", publicId);
                    var errorResponse = ErrorResponse.Create(ErrorCodes.RESOURCE_NOT_FOUND, ErrorMessages.RESOURCE_NOT_FOUND, ctx.TraceIdentifier);
                    return Results.Json(errorResponse, statusCode: 404);
                }

                Log.Information("Flat deleted successfully {PublicId}", publicId);
                return Results.Ok(ApiResponse<EmptyResponse>.Success(null, "Flat deleted successfully"));
            })
            .AddEndpointFilter<SubscriptionActiveFilter>()
            .AddEndpointFilter<ViewerForbiddenFilter>()
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .Produces<ApiResponse<EmptyResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(404)
            .Produces<ErrorResponse>(500);

            // Get Flat Ledger
            app.MapGet("/{publicId:guid}/ledger", [Authorize("ActiveSubscription")]
            [SwaggerOperation(
                Summary = "Get Flat Ledger",
                Description = "Retrieves flat ledger with all maintenance charges, payments, and running balance."
            )]
            async (Guid publicId, 
                   [FromQuery] DateTime? startDate, 
                   [FromQuery] DateTime? endDate,
                   [FromServices] IFlatService service, 
                   HttpContext ctx) =>
            {
                var userId = ctx.GetUserId();
                
                if (userId == 0)
                {
                    Log.Warning("Unauthorized flat ledger request - invalid user ID");
                    var errorResponse = ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, "Invalid or missing authentication token", ctx.TraceIdentifier);
                    return Results.Json(errorResponse, statusCode: 401);
                }

                var ledger = await service.GetFlatLedgerAsync(publicId, userId, startDate, endDate);
                Log.Information("Flat ledger retrieved for flat {PublicId}", publicId);
                return Results.Ok(ApiResponse<FlatLedgerResponse>.Success(ledger, "Flat ledger retrieved successfully"));
            })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .Produces<ApiResponse<FlatLedgerResponse>>(200)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(404)
            .Produces<ErrorResponse>(500);

            // Get Flat Financial Summary
            app.MapGet("/{publicId:guid}/financial-summary", [Authorize("ActiveSubscription")]
            [SwaggerOperation(
                Summary = "Get Flat Financial Summary",
                Description = "Retrieves flat financial summary with opening balance, monthly charges, payments, and outstanding amount.\n\nAmounts are signed: Positive = member owes the society; Negative = society owes the member (advance)."
            )]
            async (Guid publicId, [FromServices] IFlatService service, HttpContext ctx) =>
            {
                var userId = ctx.GetUserId();
                
                if (userId == 0)
                {
                    Log.Warning("Unauthorized flat financial summary request - invalid user ID");
                    var errorResponse = ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, "Invalid or missing authentication token", ctx.TraceIdentifier);
                    return Results.Json(errorResponse, statusCode: 401);
                }

                var summary = await service.GetFlatFinancialSummaryAsync(publicId, userId);
                Log.Information("Flat financial summary retrieved for flat {PublicId}", publicId);
                return Results.Ok(ApiResponse<FlatFinancialSummaryResponse>.Success(summary, "Flat financial summary retrieved successfully"));
            })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .Produces<ApiResponse<FlatFinancialSummaryResponse>>(200)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(404)
            .Produces<ErrorResponse>(500);

            // POST /flats/financial-summary/bulk
            app.MapPost("/financial-summary/bulk", [Authorize("ActiveSubscription")]
            [SwaggerOperation(
                Summary = "Bulk Get Flat Financial Summaries",
                Description = "Returns financial summaries for multiple flats in a single call. Capped at 500 IDs. Unknown or cross-society IDs are silently skipped.\n\nAmounts are signed: Positive = member owes the society; Negative = society owes the member (advance)."
            )]
            async ([FromBody] BulkFinancialSummaryRequest request, [FromServices] IFlatService service, HttpContext ctx) =>
            {
                var userId = ctx.GetUserId();
                if (userId == 0)
                {
                    var errorResponse = ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, "Invalid or missing authentication token", ctx.TraceIdentifier);
                    return Results.Json(errorResponse, statusCode: 401);
                }

                if (request?.FlatPublicIds == null || request.FlatPublicIds.Count == 0)
                    return Results.BadRequest(ApiResponse<object>.Fail("flatPublicIds must not be empty"));

                if (request.FlatPublicIds.Count > 500)
                    return Results.BadRequest(ApiResponse<object>.Fail("flatPublicIds is limited to 500 entries per request"));

                var result = await service.GetBulkFinancialSummaryAsync(request.FlatPublicIds, userId);
                Log.Information("Bulk financial summary returned {Count} entries for user {UserId}", result.Summaries.Count, userId);
                return Results.Ok(ApiResponse<BulkFinancialSummaryResponse>.Success(result, "Bulk financial summaries retrieved successfully"));
            })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("BulkGetFlatFinancialSummaries")
            .Produces<ApiResponse<BulkFinancialSummaryResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(500);

            app.MapGet("/statuses",
            [SwaggerOperation(
                    Summary = "Get flat statuses",
                    Description = "Returns all flat statuses for use in dropdowns (code + display name)."
                )]
            async ([FromServices] IFlatService service, HttpContext ctx) =>
                {
                    var list = await service.GetAllAsync();
                    if (list == null || !list.Any())
                    {
                        Log.Warning("No flat statuses found");
                        var errorResponse = ErrorResponse.Create(ErrorCodes.RESOURCE_NOT_FOUND, ErrorMessages.RESOURCE_NOT_FOUND, ctx.TraceIdentifier);
                        return Results.Json(errorResponse, statusCode: 404);
                    }

                    Log.Information("Flat statuses fetched successfully (count: {Count})", list.Count());
                    return Results.Ok(ApiResponse<FlatStatusesResponse>.Success(
                        new FlatStatusesResponse { Statuses = list.ToList() },
                        "Flat statuses retrieved successfully"));
                })
                .WithTags(groupName)
                .WithApiVersionSet(versionSet)
                .HasApiVersion(version_1_0)
                .Produces<ApiResponse<FlatStatusesResponse>>(200)
                .Produces<ErrorResponse>(400)
                .Produces<ErrorResponse>(404)
                .Produces<ErrorResponse>(500);

            // POST /flats/bulk
            app.MapPost("/bulk", [Authorize("ActiveSubscription")]
            [SwaggerOperation(
                Summary = "Bulk Create Flats",
                Description = "Creates multiple flats in a single call. Each flat is processed independently — failures do not abort the batch. Returns succeeded and failed arrays. Set skipBilling=true to skip bill generation for new flats."
            )]
            async ([FromBody] BulkCreateFlatsRequest request,
                   [FromServices] IFlatService service,
                   HttpContext ctx) =>
            {
                var userId = ctx.GetUserId();
                if (userId == 0)
                {
                    Log.Warning("Unauthorized bulk flat create request - invalid user ID");
                    var errorResponse = ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, "Invalid or missing authentication token", ctx.TraceIdentifier);
                    return Results.Json(errorResponse, statusCode: 401);
                }

                var result = await service.BulkCreateAsync(request, userId, skipBilling: request.SkipBilling);
                Log.Information("Bulk flat create completed: {SucceededCount} succeeded, {FailedCount} failed", result.Succeeded.Count, result.Failed.Count);
                return Results.Ok(ApiResponse<BulkCreateFlatsResponse>.Success(result, "Bulk flat creation completed"));
            })
            .AddEndpointFilter<FluentValidationFilter<BulkCreateFlatsRequest>>()
            .AddEndpointFilter<FlatLimitFilter>()
            .AddEndpointFilter<ViewerForbiddenFilter>()
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("BulkCreateFlats")
            .Produces<ApiResponse<BulkCreateFlatsResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(403)
            .Produces<ErrorResponse>(500);

        }
    }
}
