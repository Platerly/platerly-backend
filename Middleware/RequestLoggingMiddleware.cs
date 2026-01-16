using System.Diagnostics;

namespace Platerly.Api.Middleware
{
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLoggingMiddleware> _logger;

        public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var requestId = Guid.NewGuid().ToString("N")[..8];
            var stopwatch = Stopwatch.StartNew();

            // Add request ID to response headers for debugging
            context.Response.Headers["X-Request-Id"] = requestId;

            var method = context.Request.Method;
            var path = context.Request.Path;
            var query = context.Request.QueryString;
            var userAgent = context.Request.Headers.UserAgent.ToString();
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // Log request start
            _logger.LogInformation(
                "[{RequestId}] --> {Method} {Path}{Query} from {IP}",
                requestId, method, path, query, ip);

            try
            {
                await _next(context);
            }
            finally
            {
                stopwatch.Stop();
                var statusCode = context.Response.StatusCode;
                var elapsed = stopwatch.ElapsedMilliseconds;

                // Log request completion with appropriate level
                if (statusCode >= 500)
                {
                    _logger.LogError(
                        "[{RequestId}] <-- {StatusCode} in {Elapsed}ms | {Method} {Path}",
                        requestId, statusCode, elapsed, method, path);
                }
                else if (statusCode >= 400)
                {
                    _logger.LogWarning(
                        "[{RequestId}] <-- {StatusCode} in {Elapsed}ms | {Method} {Path}",
                        requestId, statusCode, elapsed, method, path);
                }
                else if (elapsed > 1000) // Log slow requests as warnings
                {
                    _logger.LogWarning(
                        "[{RequestId}] <-- {StatusCode} in {Elapsed}ms (SLOW) | {Method} {Path}",
                        requestId, statusCode, elapsed, method, path);
                }
                else
                {
                    _logger.LogInformation(
                        "[{RequestId}] <-- {StatusCode} in {Elapsed}ms | {Method} {Path}",
                        requestId, statusCode, elapsed, method, path);
                }
            }
        }
    }

    public static class RequestLoggingMiddlewareExtensions
    {
        public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app)
        {
            return app.UseMiddleware<RequestLoggingMiddleware>();
        }
    }
}
