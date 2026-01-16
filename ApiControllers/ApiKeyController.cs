using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.Extensions;
using Platerly.Api.Services.ApiKeys;

namespace Platerly.Api.ApiControllers
{
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
        /// Get all API keys for the current user
        /// </summary>
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
        /// Create a new API key
        /// </summary>
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
        /// Revoke an API key
        /// </summary>
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
        /// Get usage statistics for an API key
        /// </summary>
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
