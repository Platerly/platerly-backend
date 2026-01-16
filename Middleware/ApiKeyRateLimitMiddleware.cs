using Microsoft.Extensions.Caching.Memory;
using Platerly.Api.Services.ApiKeys;

namespace Platerly.Api.Middleware
{
    /// <summary>
    /// Middleware to enforce per-API-key rate limits.
    /// Tracks requests in memory cache and returns 429 when limit exceeded.
    /// </summary>
    public class ApiKeyRateLimitMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IMemoryCache _cache;
        private readonly ILogger<ApiKeyRateLimitMiddleware> _logger;

        public ApiKeyRateLimitMiddleware(
            RequestDelegate next,
            IMemoryCache cache,
            ILogger<ApiKeyRateLimitMiddleware> logger)
        {
            _next = next;
            _cache = cache;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, IApiKeyService apiKeyService)
        {
            // Only apply to API Key authenticated requests
            var apiKeyIdClaim = context.User.FindFirst("ApiKeyId")?.Value;
            if (string.IsNullOrEmpty(apiKeyIdClaim) || !Guid.TryParse(apiKeyIdClaim, out var apiKeyId))
            {
                await _next(context);
                return;
            }

            // Get rate limit from claims (set during authentication)
            var rateLimitClaim = context.User.FindFirst("ApiKeyRateLimit")?.Value;
            var rateLimit = int.TryParse(rateLimitClaim, out var limit) ? limit : 60; // Default 60 req/min

            // Build cache key for this API key's current minute window
            var windowKey = $"apikey_rate_{apiKeyId}_{DateTime.UtcNow:yyyyMMddHHmm}";

            // Get or create counter
            var requestCount = _cache.GetOrCreate(windowKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2); // Keep for 2 mins to cover window overlap
                return 0;
            });

            if (requestCount >= rateLimit)
            {
                _logger.LogWarning("API key {ApiKeyId} rate limit exceeded ({Count}/{Limit})", apiKeyId, requestCount, rateLimit);

                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers["Retry-After"] = "60";
                context.Response.Headers["X-RateLimit-Limit"] = rateLimit.ToString();
                context.Response.Headers["X-RateLimit-Remaining"] = "0";
                context.Response.Headers["X-RateLimit-Reset"] = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds().ToString();

                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Rate limit exceeded",
                    message = $"API key rate limit of {rateLimit} requests per minute exceeded. Please wait before retrying.",
                    retryAfter = 60
                });
                return;
            }

            // Increment counter
            _cache.Set(windowKey, requestCount + 1, TimeSpan.FromMinutes(2));

            // Add rate limit headers to response
            context.Response.OnStarting(() =>
            {
                context.Response.Headers["X-RateLimit-Limit"] = rateLimit.ToString();
                context.Response.Headers["X-RateLimit-Remaining"] = Math.Max(0, rateLimit - requestCount - 1).ToString();
                context.Response.Headers["X-RateLimit-Reset"] = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds().ToString();
                return Task.CompletedTask;
            });

            await _next(context);
        }
    }

    public static class ApiKeyRateLimitMiddlewareExtensions
    {
        public static IApplicationBuilder UseApiKeyRateLimiting(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ApiKeyRateLimitMiddleware>();
        }
    }
}
