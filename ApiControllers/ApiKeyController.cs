using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.Extensions;
using Platerly.Api.Services.ApiKeys;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2C controller for managing the authenticated user's B2B API keys.
    /// Allows creating keys (scoped, rate-limited, optionally expiring), listing them
    /// (keys are shown only as prefix + "…" for security), revoking them, and
    /// viewing per-key usage statistics.
    /// The raw API key value is returned <strong>only once</strong> at creation time and
    /// is never retrievable again.
    /// Base route: <c>api/api-keys</c>.
    /// All endpoints require a valid JWT (<c>[Authorize]</c> on the class).
    /// </summary>
    [ApiController]
    [Route("api/api-keys")]
    [Authorize]
    public class ApiKeyController : ControllerBase
    {
        private readonly IApiKeyService _apiKeyService;
        private readonly ILogger<ApiKeyController> _logger;

        public ApiKeyController(IApiKeyService apiKeyService, ILogger<ApiKeyController> logger)
        {
            _apiKeyService = apiKeyService;
            _logger = logger;
        }

        private Guid? CurrentUserId() => User.GetUserIdOrNull();

        /// <summary>
        /// List all API keys belonging to the authenticated user.
        /// Keys are returned with their prefix only (e.g., <c>sk_abc123…</c>);
        /// the full key value is never returned after creation.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — array of <see cref="ApiKeyDto"/> (id, name, keyPrefix, scopes,
        /// rateLimitPerMinute, isActive, expiresAt, lastUsedAt, totalRequests, createdAt, revokedAt).<br/>
        /// <c>401</c> — unauthenticated.
        /// </returns>
        [HttpGet]
        public async Task<IActionResult> GetApiKeys(CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var keys = await _apiKeyService.GetUserApiKeysAsync(userId.Value, ct);

            return Ok(keys.Select(k => new ApiKeyDto
            {
                Id = k.Id,
                Name = k.Name,
                KeyPrefix = k.KeyPrefix + "...",
                Scopes = k.Scopes,
                RateLimitPerMinute = k.RateLimitPerMinute,
                IsActive = k.IsActive,
                ExpiresAt = k.ExpiresAt,
                LastUsedAt = k.LastUsedAt,
                TotalRequests = k.TotalRequests,
                CreatedAt = k.CreatedAt,
                RevokedAt = k.RevokedAt
            }));
        }

        /// <summary>
        /// Create a new API key for the authenticated user. The raw key value is returned
        /// once in <c>ApiKeyCreatedDto.Key</c> and must be saved securely by the caller —
        /// it cannot be retrieved again. Subsequent calls to <c>GET api/api-keys</c> show
        /// only the truncated prefix.
        /// </summary>
        /// <param name="dto">
        /// Key configuration: <c>Name</c> (label), optional <c>Scopes</c> (comma-separated),
        /// optional <c>RateLimitPerMinute</c>, optional <c>ExpiresAt</c>.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="ApiKeyCreatedDto"/> including the one-time raw key and a
        /// warning message reminding the caller to save it.<br/>
        /// <c>400</c> — key creation failed.<br/>
        /// <c>401</c> — unauthenticated.
        /// </returns>
        [HttpPost]
        public async Task<IActionResult> CreateApiKey([FromBody] CreateApiKeyDto dto, CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            try
            {
                var (key, rawKey) = await _apiKeyService.CreateApiKeyAsync(
                    userId.Value,
                    dto.Name,
                    dto.Scopes,
                    dto.RateLimitPerMinute,
                    dto.ExpiresAt,
                    ct);

                return Ok(new ApiKeyCreatedDto
                {
                    Id = key.Id,
                    Name = key.Name,
                    Key = rawKey, // Only shown once!
                    Scopes = key.Scopes,
                    RateLimitPerMinute = key.RateLimitPerMinute,
                    ExpiresAt = key.ExpiresAt,
                    CreatedAt = key.CreatedAt,
                    Warning = "Save this key securely. It will not be shown again!"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create API key for user {UserId}", userId);
                return BadRequest("Failed to create API key");
            }
        }

        /// <summary>
        /// Revoke an API key, immediately disabling it. The key must belong to the
        /// authenticated user. An optional reason can be logged for audit purposes.
        /// </summary>
        /// <param name="keyId">The API key GUID to revoke.</param>
        /// <param name="reason">Optional revocation reason (stored for audit).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ message: "API key revoked successfully" }</c>.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — key not found or does not belong to this user.
        /// </returns>
        [HttpDelete("{keyId:guid}")]
        public async Task<IActionResult> RevokeApiKey(Guid keyId, [FromQuery] string? reason = null, CancellationToken ct = default)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var revoked = await _apiKeyService.RevokeApiKeyAsync(keyId, userId.Value, reason, ct);
            if (!revoked)
                return NotFound("API key not found");

            return Ok(new { message = "API key revoked successfully" });
        }

        /// <summary>
        /// Retrieve usage statistics for a specific API key (total requests, last used timestamp,
        /// rate limit consumption, etc.). The key must belong to the authenticated user.
        /// </summary>
        /// <param name="keyId">The API key GUID.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — key statistics DTO.<br/>
        /// <c>401</c> — unauthenticated.
        /// </returns>
        [HttpGet("{keyId:guid}/stats")]
        public async Task<IActionResult> GetKeyStats(Guid keyId, CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var stats = await _apiKeyService.GetKeyStatsAsync(keyId, userId.Value, ct);
            return Ok(stats);
        }
    }

    public class ApiKeyDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string KeyPrefix { get; set; } = string.Empty;
        public string? Scopes { get; set; }
        public int RateLimitPerMinute { get; set; }
        public bool IsActive { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? LastUsedAt { get; set; }
        public long TotalRequests { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? RevokedAt { get; set; }
    }

    public class ApiKeyCreatedDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string? Scopes { get; set; }
        public int RateLimitPerMinute { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Warning { get; set; } = string.Empty;
    }

    public class CreateApiKeyDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Scopes { get; set; }
        public int? RateLimitPerMinute { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }
}
