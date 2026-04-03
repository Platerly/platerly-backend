using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;

namespace Platerly.Api.Services.ApiKeys
{
    public interface IApiKeyService
    {
        Task<(ApiKey key, string rawKey)> CreateApiKeyAsync(Guid userId, string name, string? scopes = null, int? rateLimitPerMinute = null, DateTime? expiresAt = null, CancellationToken ct = default);
        Task<ApiKey?> ValidateApiKeyAsync(string rawKey, CancellationToken ct = default);
        Task<List<ApiKey>> GetUserApiKeysAsync(Guid userId, CancellationToken ct = default);
        Task<bool> RevokeApiKeyAsync(Guid keyId, Guid userId, string? reason = null, CancellationToken ct = default);
        Task LogUsageAsync(Guid apiKeyId, string endpoint, string method, int statusCode, int responseTimeMs, string? ipAddress, CancellationToken ct = default);
        Task<ApiKeyStats> GetKeyStatsAsync(Guid keyId, Guid userId, CancellationToken ct = default);
    }

    public class ApiKeyStats
    {
        public long TotalRequests { get; set; }
        public long RequestsToday { get; set; }
        public long RequestsThisMonth { get; set; }
        public DateTime? LastUsedAt { get; set; }
        public double AvgResponseTimeMs { get; set; }
        public Dictionary<string, int> RequestsByEndpoint { get; set; } = new();
    }

    /// <summary>
    /// B2B infrastructure layer — creates, validates, revokes, and logs usage for B2B API keys.
    /// Depends on <see cref="PlaterlyDbContext"/>.
    ///
    /// Key format: <c>pl_live_{url-safe-base64}</c> where the base64 encodes 32 cryptographically
    /// random bytes. Only the first 16 characters (prefix) and a SHA-256 hash of the full key are
    /// stored; the raw key is returned exactly once at creation and is never retrievable again.
    /// Validation performs a prefix-first lookup (<c>KeyPrefix</c>) to reduce the candidate set
    /// before comparing the SHA-256 hash, avoiding a full table scan.
    /// </summary>
    public class ApiKeyService : IApiKeyService
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<ApiKeyService> _logger;

        /// <summary>Prefix for live (production) API keys.</summary>
        private const string KEY_PREFIX_LIVE = "pl_live_";

        /// <summary>Prefix for test-mode API keys (reserved for future use).</summary>
        private const string KEY_PREFIX_TEST = "pl_test_";

        public ApiKeyService(PlaterlyDbContext db, ILogger<ApiKeyService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Creates a new B2B API key for the given user. Generates 32 cryptographically random bytes,
        /// encodes them as URL-safe base64, prepends the live prefix, and stores only the
        /// prefix (first 16 chars) and SHA-256 hash. Returns the raw key exactly once.
        /// </summary>
        /// <param name="userId">The user (B2B account) this key belongs to.</param>
        /// <param name="name">A descriptive label for the key.</param>
        /// <param name="scopes">Comma-separated scope string (default "analytics").</param>
        /// <param name="rateLimitPerMinute">Rate limit override (default 60 req/min).</param>
        /// <param name="expiresAt">Optional expiry date; null means no expiry.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The persisted <see cref="ApiKey"/> entity and the raw key string (one-time).</returns>
        public async Task<(ApiKey key, string rawKey)> CreateApiKeyAsync(
            Guid userId,
            string name,
            string? scopes = null,
            int? rateLimitPerMinute = null,
            DateTime? expiresAt = null,
            CancellationToken ct = default)
        {
            // Generate 32 random bytes and encode as URL-safe base64 (no padding)
            var keyBytes = RandomNumberGenerator.GetBytes(32);
            var keyBase64 = Convert.ToBase64String(keyBytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');

            var rawKey = $"{KEY_PREFIX_LIVE}{keyBase64}";
            var keyHash = HashKey(rawKey);
            var keyPrefix = rawKey.Substring(0, 16);

            var apiKey = new ApiKey
            {
                Name = name,
                UserId = userId,
                KeyHash = keyHash,
                KeyPrefix = keyPrefix,
                Scopes = scopes ?? "analytics",
                RateLimitPerMinute = rateLimitPerMinute ?? 60,
                ExpiresAt = expiresAt,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.ApiKeys.Add(apiKey);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Created API key {KeyId} for user {UserId}", apiKey.Id, userId);

            return (apiKey, rawKey);
        }

        public async Task<ApiKey?> ValidateApiKeyAsync(string rawKey, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(rawKey) || rawKey.Length < 16)
                return null;

            var keyPrefix = rawKey.Substring(0, 16);
            var keyHash = HashKey(rawKey);

            var apiKey = await _db.ApiKeys
                .Include(k => k.User)
                .Where(k => k.KeyPrefix == keyPrefix && k.KeyHash == keyHash)
                .FirstOrDefaultAsync(ct);

            if (apiKey == null)
                return null;

            // Check if key is active
            if (!apiKey.IsActive)
            {
                _logger.LogWarning("Attempted use of inactive API key {KeyId}", apiKey.Id);
                return null;
            }

            // Check if key is revoked
            if (apiKey.RevokedAt.HasValue)
            {
                _logger.LogWarning("Attempted use of revoked API key {KeyId}", apiKey.Id);
                return null;
            }

            // Check expiration
            if (apiKey.ExpiresAt.HasValue && apiKey.ExpiresAt.Value < DateTime.UtcNow)
            {
                _logger.LogWarning("Attempted use of expired API key {KeyId}", apiKey.Id);
                return null;
            }

            // Update last used
            apiKey.LastUsedAt = DateTime.UtcNow;
            apiKey.TotalRequests++;
            await _db.SaveChangesAsync(ct);

            return apiKey;
        }

        public async Task<List<ApiKey>> GetUserApiKeysAsync(Guid userId, CancellationToken ct = default)
        {
            return await _db.ApiKeys
                .Where(k => k.UserId == userId)
                .OrderByDescending(k => k.CreatedAt)
                .ToListAsync(ct);
        }

        public async Task<bool> RevokeApiKeyAsync(Guid keyId, Guid userId, string? reason = null, CancellationToken ct = default)
        {
            var key = await _db.ApiKeys
                .FirstOrDefaultAsync(k => k.Id == keyId && k.UserId == userId, ct);

            if (key == null)
                return false;

            key.RevokedAt = DateTime.UtcNow;
            key.RevocationReason = reason;
            key.IsActive = false;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Revoked API key {KeyId} for user {UserId}. Reason: {Reason}", keyId, userId, reason);
            return true;
        }

        public async Task LogUsageAsync(
            Guid apiKeyId,
            string endpoint,
            string method,
            int statusCode,
            int responseTimeMs,
            string? ipAddress,
            CancellationToken ct = default)
        {
            var log = new ApiKeyUsageLog
            {
                ApiKeyId = apiKeyId,
                Endpoint = endpoint.Length > 64 ? endpoint.Substring(0, 64) : endpoint,
                HttpMethod = method,
                StatusCode = statusCode,
                ResponseTimeMs = responseTimeMs,
                IpAddress = ipAddress,
                CreatedAt = DateTime.UtcNow
            };

            _db.ApiKeyUsageLogs.Add(log);
            await _db.SaveChangesAsync(ct);
        }

        public async Task<ApiKeyStats> GetKeyStatsAsync(Guid keyId, Guid userId, CancellationToken ct = default)
        {
            var key = await _db.ApiKeys
                .FirstOrDefaultAsync(k => k.Id == keyId && k.UserId == userId, ct);

            if (key == null)
                return new ApiKeyStats();

            var today = DateTime.UtcNow.Date;
            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

            var logs = await _db.ApiKeyUsageLogs
                .Where(l => l.ApiKeyId == keyId)
                .ToListAsync(ct);

            var requestsByEndpoint = logs
                .GroupBy(l => l.Endpoint)
                .ToDictionary(g => g.Key, g => g.Count());

            return new ApiKeyStats
            {
                TotalRequests = key.TotalRequests,
                RequestsToday = logs.Count(l => l.CreatedAt >= today),
                RequestsThisMonth = logs.Count(l => l.CreatedAt >= monthStart),
                LastUsedAt = key.LastUsedAt,
                AvgResponseTimeMs = logs.Any() ? logs.Average(l => l.ResponseTimeMs) : 0,
                RequestsByEndpoint = requestsByEndpoint
            };
        }

        private static string HashKey(string rawKey)
        {
            using var sha256 = SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(rawKey);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }
    }
}
