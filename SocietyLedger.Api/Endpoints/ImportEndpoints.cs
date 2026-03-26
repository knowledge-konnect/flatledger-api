using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using SocietyLedger.Api.Extensions;
using SocietyLedger.Application.DTOs.Import;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Shared;
using Swashbuckle.AspNetCore.Annotations;

namespace SocietyLedger.Api.Endpoints;

public static class ImportRoutes
{
    public static void MapImportRoutes(this RouteGroupBuilder app, string groupName, ApiVersionSet versionSet)
    {
        var version_1_0 = new ApiVersion(ApiConstants.API_VERSION_1_0);

        app.MapPost("/preview", [Authorize("ActiveSubscription")]
        [SwaggerOperation(
            Summary = "Preview CSV Import",
            Description = "Uploads a CSV file and returns headers and sample rows for mapping."
        )]
        async (HttpRequest request, [FromServices] IFileImportService importService, HttpContext ctx) =>
        {
            Log.Information("CSV import preview started. TraceId: {TraceId}", ctx.TraceIdentifier);
            if (!request.HasFormContentType)
                return Results.BadRequest(ErrorResponse.Create("INVALID_CONTENT_TYPE", "Content-Type must be multipart/form-data", ctx.TraceIdentifier));

            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file == null)
                return Results.BadRequest(ErrorResponse.Create("NO_FILE", "No file uploaded", ctx.TraceIdentifier));

            var result = await importService.PreviewFileAsync(file, ctx.TraceIdentifier);
            if (!result.Succeeded)
            {
                Log.Warning("CSV import preview failed: {Error}", result.ErrorMessage);
                return Results.BadRequest(ErrorResponse.Create(result.ErrorCode ?? "PREVIEW_FAILED", result.ErrorMessage ?? "Preview failed", ctx.TraceIdentifier));
            }
            Log.Information("CSV import preview completed. TraceId: {TraceId}", ctx.TraceIdentifier);
            return Results.Ok(result.Data);
        })
        .WithTags(groupName)
        .WithApiVersionSet(versionSet)
        .HasApiVersion(version_1_0)
        .Produces<FileImportPreviewResponse>(200)
        .Produces<ErrorResponse>(400);

        app.MapPost("/commit", [Authorize("ActiveSubscription")]
        [SwaggerOperation(
            Summary = "Commit CSV Import",
            Description = "Imports mapped CSV data into Flats, Members, and Opening Balances."
        )]
        async ([FromBody] FileImportCommitRequest request, [FromServices] IFileImportService importService, HttpContext ctx) =>
        {
            Log.Information("CSV import commit started. TraceId: {TraceId}", ctx.TraceIdentifier);
            var userId = ctx.GetUserId();
            if (userId == 0)
                return Results.Json(ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, "Invalid or missing authentication token", ctx.TraceIdentifier), statusCode: 401);

            var result = await importService.CommitImportAsync(request, userId, ctx.TraceIdentifier);
            if (!result.Succeeded)
            {
                Log.Warning("CSV import commit failed: {Error}", result.ErrorMessage);
                return Results.BadRequest(ErrorResponse.Create(result.ErrorCode ?? "IMPORT_FAILED", result.ErrorMessage ?? "Import failed", ctx.TraceIdentifier));
            }
            Log.Information("CSV import commit completed. TraceId: {TraceId}", ctx.TraceIdentifier);
            return Results.Ok(result.Data);
        })
        .WithTags(groupName)
        .WithApiVersionSet(versionSet)
        .HasApiVersion(version_1_0)
        .Produces<FileImportResultResponse>(200)
        .Produces<ErrorResponse>(400);
    }
}
