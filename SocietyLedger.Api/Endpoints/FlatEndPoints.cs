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
using SocietyLedger.Infrastructure.Services.Common;
using SocietyLedger.Domain.Constants;
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
                if (userId == 0)
                {
                    Log.Warning("Unauthorized flat create request - invalid user ID");
                    var errorResponse = ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, "Invalid or missing authentication token", ctx.TraceIdentifier);
                    return Results.Json(errorResponse, statusCode: 401);
                }

                if (ctx.GetUserRoleCode() == RoleCodes.Viewer)
                    return Results.Json(new { error = "Forbidden", message = "You do not have permission to perform this action." }, statusCode: 403);

                var result = await service.CreateAsync(request, userId);
                Log.Information("Flat created successfully for FlatNo {FlatNo}", request.FlatNo);
                return Results.Ok(ApiResponse<FlatResponseDto>.Success(result, "Flat created successfully"));
            })
            .AddEndpointFilter<FluentValidationFilter<CreateFlatDto>>()
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .Produces<ApiResponse<FlatResponseDto>>(201)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(409)
            .Produces<ErrorResponse>(500);

            app.MapGet("/",[Authorize("ActiveSubscription")]
            [SwaggerOperation(
        Summary = "Get all Flats for Current Society",
        Description = "Fetches the list of all flats that belong to the authenticated user's society."
    )]
            async (IFlatService service, IUserRepository userRepository, HttpContext ctx) =>
    {
        var userId = ctx.GetUserId();
        if (userId == 0)
        {
            Log.Warning("Unauthorized flat list request - invalid user ID");
            var errorResponse = ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, "Invalid or missing authentication token", ctx.TraceIdentifier);
            return Results.Json(errorResponse, statusCode: 401);
        }

        // Get user to extract societyId
        var user = await userRepository.GetByIdAsync(userId);
        if (user == null || !user.IsActive)
        {
            Log.Warning("Flat list request by inactive or non-existent user {UserId}", userId);
            var errorResponse = ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, "User not found or inactive", ctx.TraceIdentifier);
            return Results.Json(errorResponse, statusCode: 401);
        }

        var societyId = user.SocietyId;
        var flats = await service.GetBySocietyIdAsync(societyId);

        if (flats == null || !flats.Any())
        {
            Log.Warning("No flats found for SocietyId {SocietyId}", societyId);
            var errorResponse = ErrorResponse.Create(ErrorCodes.RESOURCE_NOT_FOUND, ErrorMessages.RESOURCE_NOT_FOUND, ctx.TraceIdentifier);
            return Results.Json(errorResponse, statusCode: 404);
        }

        Log.Information("Fetched {Count} flats for SocietyId {SocietyId}",
                        flats.Count(), societyId);

        return Results.Ok(ApiResponse<ListFlatsResponse>.Success(
            new ListFlatsResponse { Flats = flats.ToList() },
            "Flats retrieved successfully"));
    })
    .WithName("GetFlatsBySocietyId")
    .WithTags(groupName)
    .WithApiVersionSet(versionSet)
    .HasApiVersion(version_1_0)
    .Produces<ApiResponse<ListFlatsResponse>>(200)
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
                if (ctx.GetUserRoleCode() == RoleCodes.Viewer)
                    return Results.Json(new { error = "Forbidden", message = "You do not have permission to perform this action." }, statusCode: 403);
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
                if (ctx.GetUserRoleCode() == RoleCodes.Viewer)
                    return Results.Json(new { error = "Forbidden", message = "You do not have permission to perform this action." }, statusCode: 403);
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
                Description = "Retrieves flat financial summary with opening balance, monthly charges, payments, and outstanding amount."
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

            app.MapGet("/statuses",
                [Authorize("ActiveSubscription")]
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
                Description = "Creates multiple flats in a single call. Each flat is processed independently — failures do not abort the batch. Returns succeeded and failed arrays."
            )]
            async ([FromBody] BulkCreateFlatsRequest request,
                   [FromServices] IFlatService service,
                   [FromServices] IBillingService billingService,
                   [FromServices] IValidator<BulkCreateFlatItemDto> validator,
                   [FromServices] IMaintenanceConfigRepository maintenanceConfigRepo,
                   [FromServices] IUserContext userContext,
                   HttpContext ctx) =>
            {
                var userId = ctx.GetUserId();
                if (userId == 0)
                {
                    Log.Warning("Unauthorized bulk flat create request - invalid user ID");
                    var errorResponse = ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, "Invalid or missing authentication token", ctx.TraceIdentifier);
                    return Results.Json(errorResponse, statusCode: 401);
                }

                if (ctx.GetUserRoleCode() == RoleCodes.Viewer)
                    return Results.Json(new { error = "Forbidden", message = "You do not have permission to perform this action." }, statusCode: 403);

                if (request.Flats == null || request.Flats.Count == 0)
                    return Results.BadRequest(ErrorResponse.Create(ErrorCodes.VALIDATION_FAILED, "Flats list cannot be empty.", ctx.TraceIdentifier));

                // Fetch society's maintenance config once — use DefaultMonthlyCharge for all bulk flats
                var (_, societyId) = await userContext.GetUserContextAsync(userId);
                var maintenanceConfig = await maintenanceConfigRepo.GetBySocietyIdAsync(societyId);
                var defaultMaintenanceAmount = maintenanceConfig?.DefaultMonthlyCharge ?? 0m;
                Log.Information("Bulk create: using maintenance amount {Amount} from config for societyId {SocietyId}", defaultMaintenanceAmount, societyId);

                var succeeded = new List<FlatResponseDto>();
                var failed = new List<BulkFlatFailure>();

                for (int i = 0; i < request.Flats.Count; i++)
                {
                    var item = request.Flats[i];
                    var flatNo = item?.FlatNo ?? $"(index {i})";

                    try
                    {
                        // Validate each item using the bulk-specific validator (no maintenanceAmount)
                        var validationResult = await validator.ValidateAsync(item!);
                        if (!validationResult.IsValid)
                        {
                            var errorMsg = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
                            failed.Add(new BulkFlatFailure(i, flatNo, errorMsg));
                            Log.Warning("Bulk flat create validation failed at index {Index} FlatNo {FlatNo}: {Error}", i, flatNo, errorMsg);
                            continue;
                        }

                        // Map to CreateFlatDto — maintenanceAmount sourced from society maintenance config
                        var flatDto = new CreateFlatDto(item!.FlatNo, item.OwnerName, item.ContactMobile, item.ContactEmail, defaultMaintenanceAmount, item.StatusCode);
                        var createdFlat = await service.CreateAsync(flatDto, userId);
                        succeeded.Add(createdFlat);

                        // Auto-generate current-month bill for the newly created flat
                        try
                        {
                            var billingMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                            await billingService.GenerateBillForFlatAsync(createdFlat.PublicId, userId, billingMonth);
                        }
                        catch (Exception billingEx)
                        {
                            // Billing failure is non-fatal — flat was already created successfully
                            Log.Warning(billingEx, "Billing generation failed for flat {PublicId} (FlatNo {FlatNo}) during bulk create", createdFlat.PublicId, flatNo);
                        }
                    }
                    catch (Exception ex)
                    {
                        failed.Add(new BulkFlatFailure(i, flatNo, ex.Message));
                        Log.Warning(ex, "Bulk flat create failed at index {Index} FlatNo {FlatNo}", i, flatNo);
                    }
                }

                Log.Information("Bulk flat create completed: {SucceededCount} succeeded, {FailedCount} failed", succeeded.Count, failed.Count);
                return Results.Ok(new BulkCreateFlatsResponse(succeeded, failed));
            })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("BulkCreateFlats")
            .Produces<BulkCreateFlatsResponse>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(403)
            .Produces<ErrorResponse>(500);

        }
    }
}
