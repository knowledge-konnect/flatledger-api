using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SocietyLedger.Application.DTOs.Email;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Shared;
using Swashbuckle.AspNetCore.Annotations;

namespace SocietyLedger.Api.Endpoints
{
    public static class EmailGatewayEndpoints
    {
        public static void MapEmailGatewayRoutes(this RouteGroupBuilder app, string groupName, ApiVersionSet versionSet)
        {
            var version_1_0 = new ApiVersion(ApiConstants.API_VERSION_1_0);

            app.MapPost("/send",
                [Authorize]
            [SwaggerOperation(
                    Summary = "Send email via gateway",
                    Description = "Sends a generic email using the configured email gateway."
                )]
            async ([FromBody] GenericEmailRequest request, IEmailGatewayService gatewayService, HttpContext ctx) =>
                {
                    if (string.IsNullOrWhiteSpace(request.ToEmail)
                        || string.IsNullOrWhiteSpace(request.Subject)
                        || string.IsNullOrWhiteSpace(request.HtmlContent))
                    {
                        return Results.BadRequest(
                            ErrorResponse.Create(
                                ErrorCodes.VALIDATION_FAILED,
                                "toEmail, subject and htmlContent are required.",
                                ctx.TraceIdentifier));
                    }

                    var sent = await gatewayService.SendGenericEmailAsync(
                        request.ToEmail,
                        request.Subject,
                        request.HtmlContent,
                        ctx.RequestAborted);

                    if (!sent)
                    {
                        return Results.BadRequest(
                            ErrorResponse.Create(
                                ErrorCodes.VALIDATION_FAILED,
                                "Email send failed.",
                                ctx.TraceIdentifier));
                    }

                    var response = new EmailGatewayResponse
                    {
                        Success = true,
                        Message = "Email processed."
                    };

                    return Results.Ok(ApiResponse<EmailGatewayResponse>.Success(response, "Email processed"));
                })
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("SendEmailGateway")
            .Produces<ApiResponse<EmailGatewayResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(500);
        }
    }
}
