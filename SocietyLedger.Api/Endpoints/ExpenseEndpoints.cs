using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using SocietyLedger.Api.Extensions;
using SocietyLedger.Api.Filters;
using SocietyLedger.Application.DTOs.Expense;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Shared;
using Swashbuckle.AspNetCore.Annotations;

namespace SocietyLedger.Api.Endpoints
{
    public static class ExpenseEndpoints
    {
        /// <summary>
        /// Maps expense routes: create, retrieve, update, and delete society expenses.
        /// </summary>
        public static void MapExpenseRoutes(this RouteGroupBuilder app, string groupName, ApiVersionSet versionSet)
        {
            var version_1_0 = new ApiVersion(ApiConstants.API_VERSION_1_0);

            app.MapPost("/",
                [Authorize]
            [SwaggerOperation(
                    Summary = "Create expense",
                    Description = "Records a new expense for the society."
                )]
            async ([FromBody] CreateExpenseRequest request, IExpenseService expenseService, HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();
                    var result = await expenseService.CreateExpenseAsync(userId, request);
                    Log.Information("Expense created successfully by user {UserId}", userId);
                    return Results.Created($"/expenses/{result.PublicId}", ApiResponse<ExpenseResponse>.Success(result, "Expense created successfully"));
                })
            .AddEndpointFilter<FluentValidationFilter<CreateExpenseRequest>>()
            .AddEndpointFilter<SubscriptionActiveFilter>()
            .AddEndpointFilter<ViewerForbiddenFilter>()
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("CreateExpense")
            .Produces<ApiResponse<ExpenseResponse>>(201)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(500);

            // Get all expenses for society (with optional pagination/filtering)
            app.MapGet("/",
                [Authorize]
            [SwaggerOperation(
                    Summary = "Get expenses",
                    Description = "Retrieves expenses for the society. Supports optional pagination, date range, category, and search filters. When no params are provided, returns all expenses (backward compatible)."
                )]
            async (
                IExpenseService expenseService,
                HttpContext ctx,
                [FromQuery] DateOnly? startDate,
                [FromQuery] DateOnly? endDate,
                [FromQuery] string? categoryCode,
                [FromQuery] string? search,
                [FromQuery] int? page,
                [FromQuery] int? size,
                [FromQuery] string? sortBy,
                [FromQuery] string? sortDir) =>
                {
                    var userId = ctx.GetUserId();

                    // Validate date range
                    if (startDate.HasValue && endDate.HasValue && startDate > endDate)
                        return Results.BadRequest(ApiResponse<object>.Fail("startDate must be before or equal to endDate"));

                    // Validate sortBy
                    var allowedSortBy = new[] { "dateIncurred", "amount", "categoryCode" };
                    var resolvedSortBy = sortBy ?? "dateIncurred";
                    if (!allowedSortBy.Contains(resolvedSortBy, StringComparer.OrdinalIgnoreCase))
                        return Results.BadRequest(ApiResponse<object>.Fail($"Invalid sortBy value '{resolvedSortBy}'. Allowed: {string.Join(", ", allowedSortBy)}"));

                    var resolvedSortDir = sortDir ?? "desc";
                    if (!new[] { "asc", "desc" }.Contains(resolvedSortDir, StringComparer.OrdinalIgnoreCase))
                        return Results.BadRequest(ApiResponse<object>.Fail("Invalid sortDir value. Allowed: asc, desc"));

                    // If no pagination/filter params are provided, return the full unpaginated list
                    // to preserve backward compatibility with existing clients.
                    if (page == null && size == null && startDate == null && endDate == null && categoryCode == null && search == null && sortBy == null)
                    {
                        var all = await expenseService.GetExpensesBySocietyAsync(userId);
                        return Results.Ok(ApiResponse<ListExpensesResponse>.Success(
                            new ListExpensesResponse { Expenses = all.ToList() },
                            "Expenses retrieved successfully"));
                    }

                    var resolvedPage = page ?? 0;
                    var resolvedSize = Math.Min(size ?? 25, 100);

                    if (resolvedPage < 0)
                        return Results.BadRequest(ApiResponse<object>.Fail("page must be >= 0"));
                    if (resolvedSize <= 0)
                        return Results.BadRequest(ApiResponse<object>.Fail("size must be > 0"));

                    var result = await expenseService.GetPagedAsync(
                        userId, startDate, endDate, categoryCode, search,
                        resolvedPage, resolvedSize, resolvedSortBy, resolvedSortDir);

                    return Results.Ok(ApiResponse<PagedExpensesResponse>.Success(result, "Expenses retrieved successfully"));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("GetExpenses")
            .Produces<ApiResponse<ListExpensesResponse>>(200)
            .Produces<ApiResponse<PagedExpensesResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(500);

            // Get expense by ID
            app.MapGet("/{publicId:guid}",
                [Authorize]
            [SwaggerOperation(
                    Summary = "Get expense by ID",
                    Description = "Retrieves a specific expense by its public ID."
                )]
            async (Guid publicId, IExpenseService expenseService, HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();
                    var result = await expenseService.GetExpenseAsync(publicId, userId);
                    return Results.Ok(ApiResponse<ExpenseResponse>.Success(result, "Expense retrieved successfully"));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("GetExpenseById")
            .Produces<ApiResponse<ExpenseResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(500);

            // Get expenses by date range
            app.MapGet("/range",
                [Authorize]
            [SwaggerOperation(
                    Summary = "Get expenses by date range",
                    Description = "Retrieves expenses for a specific date range."
                )]
            async ([FromQuery] DateOnly startDate, [FromQuery] DateOnly endDate, IExpenseService expenseService, HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();
                    var result = await expenseService.GetExpensesByDateRangeAsync(userId, startDate, endDate);
                    return Results.Ok(ApiResponse<ListExpensesResponse>.Success(
                        new ListExpensesResponse { Expenses = result.ToList() },
                        "Expenses retrieved successfully"));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("GetExpensesByDateRange")
            .Produces<ApiResponse<ListExpensesResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(500);

            // Get expense categories
            app.MapGet("/categories",
                [Authorize]
            [SwaggerOperation(
                    Summary = "Get expense categories",
                    Description = "Retrieves all available expense categories."
                )]
            async (IExpenseService expenseService, HttpContext ctx) =>
                {
                    var result = await expenseService.GetExpenseCategoriesAsync();
                    return Results.Ok(ApiResponse<ExpenseCategoriesResponse>.Success(
                        new ExpenseCategoriesResponse { Categories = result.ToList() },
                        "Expense categories retrieved successfully"));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("GetExpenseCategories")
            .Produces<ApiResponse<ExpenseCategoriesResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(500);

            // Get expenses by category
            app.MapGet("/category/{categoryCode}",
                [Authorize]
            [SwaggerOperation(
                    Summary = "Get expenses by category",
                    Description = "Retrieves all expenses for a specific category."
                )]
            async (string categoryCode, IExpenseService expenseService, HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();
                    var result = await expenseService.GetExpensesByCategoryAsync(userId, categoryCode);
                    return Results.Ok(ApiResponse<ListExpensesResponse>.Success(
                        new ListExpensesResponse { Expenses = result.ToList() },
                        "Expenses retrieved successfully"));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("GetExpensesByCategory")
            .Produces<ApiResponse<ListExpensesResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(500);

            // Update expense
            app.MapPut("/{publicId:guid}",
                [Authorize]
            [SwaggerOperation(
                    Summary = "Update expense",
                    Description = "Updates an existing expense."
                )]
            async (Guid publicId, [FromBody] UpdateExpenseRequest request, IExpenseService expenseService, HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();
                    var result = await expenseService.UpdateExpenseAsync(publicId, userId, request);
                    return Results.Ok(ApiResponse<ExpenseResponse>.Success(result, "Expense updated successfully"));
                })
            .AddEndpointFilter<FluentValidationFilter<UpdateExpenseRequest>>()
            .AddEndpointFilter<SubscriptionActiveFilter>()
            .AddEndpointFilter<ViewerForbiddenFilter>()
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("UpdateExpense")
            .Produces<ApiResponse<ExpenseResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(500);

            // Delete expense
            app.MapDelete("/{publicId:guid}",
                [Authorize]
            [SwaggerOperation(
                    Summary = "Delete expense",
                    Description = "Deletes an expense."
                )]
            async (Guid publicId, IExpenseService expenseService, HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();
                    await expenseService.DeleteExpenseAsync(publicId, userId);
                    return Results.Ok(ApiResponse<EmptyResponse>.Success(new EmptyResponse(), "Expense deleted successfully"));
                })
            .AddEndpointFilter<SubscriptionActiveFilter>()
            .AddEndpointFilter<ViewerForbiddenFilter>()
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("DeleteExpense")
            .Produces<ApiResponse<EmptyResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(500);
        }
    }
}
