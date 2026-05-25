using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using SocietyLedger.Api.Extensions;
using SocietyLedger.Api.Filters;
using SocietyLedger.Application.DTOs.Razorpay;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Shared;
using Swashbuckle.AspNetCore.Annotations;
using System.Text;
using System.Text.Json;

namespace SocietyLedger.Api.Endpoints
{
    public static class PaymentEndpoints
    {
        /// <summary>
        /// Maps Razorpay payment routes: create order, verify payment, and handle webhooks.
        /// </summary>
        public static void MapPaymentRoutes(this RouteGroupBuilder app, string groupName, ApiVersionSet versionSet)
        {
            var version_1_0 = new ApiVersion(ApiConstants.API_VERSION_1_0);

            // Create Razorpay order
            app.MapPost("/create-order",
                [Authorize]
            [SwaggerOperation(
                    Summary = "Create Razorpay payment order",
                    Description = "Creates a Razorpay order for subscription payment."
                )]
            async ([FromBody] CreateOrderRequest request, IRazorpayPaymentService paymentService, HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();
                    var result = await paymentService.CreateOrderAsync(userId, request.PlanId);
                    return Results.Created("/payments/verify-payment", ApiResponse<CreateOrderResponse>.Success(result, "Order created successfully"));
                })
            .AddEndpointFilter<FluentValidationFilter<CreateOrderRequest>>()
            .AddEndpointFilter<ViewerForbiddenFilter>()
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("CreateOrder")
            .Produces<ApiResponse<CreateOrderResponse>>(201)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(500);

            // Verify payment
            app.MapPost("/verify-payment",
                [Authorize]
            [SwaggerOperation(
                    Summary = "Verify Razorpay payment",
                    Description = "Verifies payment signature and activates subscription."
                )]
            async ([FromBody] VerifyPaymentRequest request, IRazorpayPaymentService paymentService, HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();
                    if (userId == 0)
                        return Results.Json(ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, ErrorMessages.UNAUTHORIZED, ctx.TraceIdentifier), statusCode: 401);
                    var result = await paymentService.VerifyPaymentAsync(request, userId);
                    return Results.Ok(ApiResponse<VerifyPaymentResponse>.Success(result, "Payment verification completed"));
                })
            .AddEndpointFilter<FluentValidationFilter<VerifyPaymentRequest>>()
            .AddEndpointFilter<ViewerForbiddenFilter>()
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("VerifyPayment")
            .Produces<ApiResponse<VerifyPaymentResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(500);

            // Webhook endpoint (no auth — protected by HMAC-SHA256 signature verification inside the service)
            app.MapPost("/webhook",
                [AllowAnonymous]
            [SwaggerOperation(
                    Summary = "Razorpay webhook",
                    Description = "Handles Razorpay payment events. Signature is verified server-side using X-Razorpay-Signature header."
                )]
            async (HttpRequest req, IRazorpayPaymentService paymentService) =>
                {
                    req.EnableBuffering();
                    using var reader = new StreamReader(req.Body, Encoding.UTF8, leaveOpen: true);
                    var rawBody = await reader.ReadToEndAsync();
                    req.Body.Position = 0;

                    var signature = req.Headers["X-Razorpay-Signature"].ToString();

                    WebhookPayload? payload;
                    try
                    {
                        payload = JsonSerializer.Deserialize<WebhookPayload>(
                            rawBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    catch (JsonException)
                    {
                        Log.Warning("Webhook: failed to deserialize payload");
                        return Results.BadRequest(ErrorResponse.Create(ErrorCodes.VALIDATION_FAILED, "Webhook payload could not be parsed. Ensure the request body is valid JSON.", null));
                    }

                    if (payload == null)
                        return Results.BadRequest(ErrorResponse.Create(ErrorCodes.VALIDATION_FAILED, "Webhook payload is empty.", null));

                    var result = await paymentService.ProcessWebhookAsync(rawBody, signature, payload);

                    return result.Status switch
                    {
                        WebhookProcessStatus.Processed or WebhookProcessStatus.Duplicate or WebhookProcessStatus.Ignored =>
                            Results.Ok(ApiResponse<string>.Success("ok", result.Message)),

                        WebhookProcessStatus.InvalidSignature =>
                            Results.Json(
                                ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, result.Message, null),
                                statusCode: 401),

                        WebhookProcessStatus.InvalidPayload =>
                            Results.BadRequest(
                                ErrorResponse.Create(ErrorCodes.VALIDATION_FAILED, result.Message, null)),

                        WebhookProcessStatus.OrderNotFound =>
                            Results.NotFound(
                                ErrorResponse.Create(ErrorCodes.RESOURCE_NOT_FOUND, result.Message, null)),

                        _ => Results.BadRequest(
                            ErrorResponse.Create(ErrorCodes.VALIDATION_FAILED, "Webhook could not be processed.", null))
                    };
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("PaymentWebhook")
            .Produces<ApiResponse<string>>(200);
        }
    }
}