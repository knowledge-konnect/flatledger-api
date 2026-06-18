using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using SocietyLedger.Api.Extensions;
using SocietyLedger.Api.Filters;
using SocietyLedger.Application.DTOs.ContactUs;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Shared;
using Swashbuckle.AspNetCore.Annotations;

namespace SocietyLedger.Api.Endpoints
{
    public static class ContactUsRoutes
    {
        /// <summary>
        /// Maps contact us routes: submit contact us form.
        /// </summary>
        public static void MapContactUsRoutes(this RouteGroupBuilder app, string groupName, ApiVersionSet versionSet)
        {
            var version_1_0 = new ApiVersion(ApiConstants.API_VERSION_1_0);

            // POST /contact-us
            app.MapPost("/",
                [AllowAnonymous]
                [SwaggerOperation(
                    Summary = "Submit contact us form",
                    Description = "Submits a contact us form and sends a notification to the support team."
                )]
                async ([FromBody] ContactUsRequest request, IContactUsService contactUsService, HttpContext ctx) =>
                {
                    var ip = ctx.GetClientIp();
                    var result = await contactUsService.SubmitContactUsAsync(request, ip);
                    
                    if (result)
                    {
                        return Results.Ok(ApiResponse<EmptyResponse>.Success(null, "Thank you for contacting us. We will get back to you soon."));
                    }
                    else
                    {
                        var errorResponse = ErrorResponse.Create("EMAIL_SEND_FAILED", "Failed to send your message. Please try again later.", ctx.TraceIdentifier);
                        return Results.Json(errorResponse, statusCode: 500);
                    }
                })
            .AddEndpointFilter<FluentValidationFilter<ContactUsRequest>>()
            .RequireRateLimiting("AuthPolicy")
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("SubmitContactUs")
            .Produces<ApiResponse<EmptyResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(500);
        }
    }
}