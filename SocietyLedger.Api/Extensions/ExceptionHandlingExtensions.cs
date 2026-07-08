using Microsoft.AspNetCore.Diagnostics;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Shared;
using System.Net;

namespace SocietyLedger.Api.Extensions;

public static class ExceptionHandlingExtensions
{
    public static WebApplication UseApiExceptionHandling(this WebApplication app)
    {
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
                if (exception is null)
                    return;

                var correlationId = context.Request.Headers.TryGetValue("X-Correlation-ID", out var headerCorrelationId)
                    ? (string)headerCorrelationId!
                    : context.TraceIdentifier;

                var (statusCode, response) = MapException(exception, correlationId);

                context.Response.ContentType = "application/json";
                context.Response.StatusCode = (int)statusCode;
                await context.Response.WriteAsJsonAsync(response);
            });
        });

        return app;
    }

    private static (HttpStatusCode, ErrorResponse) MapException(Exception exception, string correlationId)
    {
        return exception switch
        {
            ValidationException vex => (
                HttpStatusCode.BadRequest,
                ErrorResponse.CreateWithFields(
                    vex.Code,
                    vex.Message,
                    vex.Errors.Select(e => new FieldError { Field = e.Key, Messages = e.Value }).ToList(),
                    correlationId
                )
            ),
            AuthenticationException aex => (
                HttpStatusCode.Unauthorized,
                ErrorResponse.Create(aex.Code, aex.Message, correlationId)
            ),
            AuthorizationException azex => (
                HttpStatusCode.Forbidden,
                ErrorResponse.Create(azex.Code, azex.Message, correlationId)
            ),
            NotFoundException nex => (
                HttpStatusCode.NotFound,
                ErrorResponse.Create(nex.Code, nex.Message, correlationId)
            ),
            ConflictException cex => (
                HttpStatusCode.Conflict,
                ErrorResponse.Create(cex.Code, cex.Message, correlationId)
            ),
            DuplicateException dex => (
                HttpStatusCode.Conflict,
                ErrorResponse.Create(dex.Code, dex.Message, correlationId)
            ),
            AppException aex => (
                HttpStatusCode.BadRequest,
                ErrorResponse.Create(aex.Code, aex.Message, correlationId)
            ),
            _ => (
                HttpStatusCode.InternalServerError,
                ErrorResponse.Create(
                    ErrorCodes.INTERNAL_SERVER_ERROR,
                    ErrorMessages.INTERNAL_SERVER_ERROR,
                    correlationId
                )
            )
        };
    }
}
