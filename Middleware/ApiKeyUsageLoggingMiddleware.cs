using System.Diagnostics;
using Platerly.Api.Services.ApiKeys;

namespace Platerly.Api.Middleware
{
    /// <summary>
    /// Middleware to log API key usage for analytics and billing.
    /// Tracks endpoint, method, status code, and response time.
    /// </summary>
    public class ApiKeyUsageLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ApiKeyUsageLoggingMiddleware> _logger;

        public ApiKeyUsageLoggingMiddleware(
            RequestDelegate next,
            ILogger<ApiKeyUsageLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, IApiKeyService apiKeyService)
        {
            // Only log for API Key authenticated requests
            var apiKeyIdClaim = context.User.FindFirst("ApiKeyId")?.Value;
            if (string.IsNullOrEmpty(apiKeyIdClaim) || !Guid.TryParse(apiKeyIdClaim, out var apiKeyId))
            {
                await _next(context);
                return;
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                await _next(context);
            }
            finally
            {
                stopwatch.Stop();

                // Log usage asynchronously (fire and forget to not slow down response)
                var endpoint = context.Request.Path.Value ?? "/";
                var method = context.Request.Method;
                var statusCode = context.Response.StatusCode;
                var responseTimeMs = (int)stopwatch.ElapsedMilliseconds;
                var ipAddress = context.Connection.RemoteIpAddress?.ToString();

                // Fire and forget - don't await to avoid slowing response
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = context.RequestServices.CreateScope();
                        var service = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
                        await service.LogUsageAsync(apiKeyId, endpoint, method, statusCode, responseTimeMs, ipAddress);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to log API key usage for {ApiKeyId}", apiKeyId);
                    }
                });
            }
        }
    }

    public static class ApiKeyUsageLoggingMiddlewareExtensions
    {
        public static IApplicationBuilder UseApiKeyUsageLogging(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ApiKeyUsageLoggingMiddleware>();
        }
    }
}
