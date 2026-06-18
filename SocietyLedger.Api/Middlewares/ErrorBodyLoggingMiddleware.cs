using Serilog;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SocietyLedger.Api.Middlewares
{
    /// <summary>
    /// Middleware for capturing and logging request/response bodies on error responses (4xx, 5xx).
    /// Useful for debugging client-side issues and server failures.
    /// Request body is buffered to enable re-reading by downstream middleware.
    /// </summary>
    public class ErrorBodyLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ErrorBodyLoggingMiddleware> _logger;
        private const int MaxBodyLength = 4096; // Limit body capture to 4KB

        public ErrorBodyLoggingMiddleware(RequestDelegate next, ILogger<ErrorBodyLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Buffer request body for error logging and downstream middleware
            var requestBodyContent = await ReadRequestBodyAsync(context.Request);
            context.Request.Body.Position = 0; // Reset position for downstream middleware

            // Buffer response body
            var originalResponseBody = context.Response.Body;
            using var responseBodyBuffer = new MemoryStream();
            context.Response.Body = responseBodyBuffer;

            try
            {
                await _next(context);
            }
            finally
            {
                // Log body on error
                if (context.Response.StatusCode >= 400)
                {
                    await responseBodyBuffer.FlushAsync();
                    var responseBodyContent = Encoding.UTF8.GetString(responseBodyBuffer.ToArray());

                    var correlationId = context.Request.Headers.TryGetValue("X-Correlation-ID", out var headerCorrelationId)
                        ? (string)headerCorrelationId!
                        : context.TraceIdentifier;

                    _logger.LogWarning(
                        "Error response captured | Status: {StatusCode} | CorrelationId: {CorrelationId} | RequestBody: {RequestBody} | ResponseBody: {ResponseBody}",
                        context.Response.StatusCode,
                        correlationId,
                        SanitizeBody(requestBodyContent),
                        SanitizeBody(responseBodyContent));
                }

                // Copy buffered response back to original response
                responseBodyBuffer.Seek(0, SeekOrigin.Begin);
                await responseBodyBuffer.CopyToAsync(originalResponseBody);
            }
        }

        private static async Task<string> ReadRequestBodyAsync(HttpRequest request)
        {
            request.EnableBuffering();
            using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            request.Body.Position = 0; // Reset for next read
            return body.Length > MaxBodyLength ? body[..MaxBodyLength] + "..." : body;
        }

        private static string SanitizeBody(string body)
        {
            // Remove sensitive values from logging (password, token, auth headers, signatures, secrets).
            if (string.IsNullOrEmpty(body))
                return "[empty]";

            try
            {
                // Regex pattern: match sensitive keys and mask their values
                // Pattern matches: "password": "value" or password:"value" or password=value
                var sanitized = Regex.Replace(body,
                    @"(?<key>""?(?:password|token|refreshToken|authorization|accessToken|idToken|signature|secret)""?\s*[:=]\s*)(?<value>""[^""]*""|[^,\r\n}]*)",
                    "${key}***",
                    RegexOptions.IgnoreCase);

                // Also mask header-style values: Authorization: Bearer ... → Authorization: ***
                sanitized = Regex.Replace(sanitized,
                    @"(?<header>(?:authorization|x-razorpay-signature)\s*[:=]\s*)([^\r\n,}]+)",
                    "${header}***",
                    RegexOptions.IgnoreCase);

                return sanitized.Length > MaxBodyLength
                    ? sanitized[..MaxBodyLength] + "..."
                    : sanitized;
            }
            catch
            {
                return "[unable to sanitize]";
            }
        }
    }
}
