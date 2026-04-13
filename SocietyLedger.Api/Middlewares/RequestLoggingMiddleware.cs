// RequestLoggingMiddleware has been removed.
// Per-request logging is now handled by app.UseSerilogRequestLogging() in Program.cs,
// which emits one structured log line per request (Method, Path, StatusCode, Elapsed, UserId, CorrelationId)
// and automatically suppresses noise from /health and /swagger endpoints.
// This file is intentionally left as a comment to aid git-blame traceability.

