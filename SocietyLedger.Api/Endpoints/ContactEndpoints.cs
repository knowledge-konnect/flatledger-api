using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using SocietyLedger.Api.Filters;
using SocietyLedger.Application.DTOs.Contact;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Shared;
using Swashbuckle.AspNetCore.Annotations;

namespace SocietyLedger.Api.Endpoints
{
    public static class ContactEndpoints
    {
        public static void MapContactRoutes(this RouteGroupBuilder app, string groupName, ApiVersionSet versionSet)
        {
            var version_1_0 = new ApiVersion(ApiConstants.API_VERSION_1_0);

            app.MapPost("/",
                [AllowAnonymous]
                [SwaggerOperation(
                    Summary = "Contact us",
                    Description = "Submits a contact-us form. The submission is saved to the database and a notification email is sent to the support team."
                )]
                async ([FromBody] ContactUsRequest request, IContactService contactService, HttpContext ctx) =>
                {
                    await contactService.SubmitAsync(request, ctx.RequestAborted);

                    Log.Information(
                        "Contact-us submission from {Email}",
                        request.Email);

                    return Results.Ok(ApiResponse<EmptyResponse>.Success(null,
                        "Thank you for reaching out. We'll get back to you shortly."));
                })
            .AddEndpointFilter<FluentValidationFilter<ContactUsRequest>>()
            .RequireRateLimiting("AuthPolicy")
            .WithTags(groupName)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(version_1_0)
            .WithName("ContactUs")
            .Produces<ApiResponse<EmptyResponse>>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(429)
            .Produces<ErrorResponse>(500);
        }
    }
}
