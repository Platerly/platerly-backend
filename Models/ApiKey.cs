using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Platerly.Api.Models
{
    /// <summary>
    /// API Key for B2B customers to access analytics endpoints.
    /// </summary>
    public class ApiKey
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// The hashed API key (never store raw keys)
        /// </summary>
        [Required, MaxLength(128)]
        public string KeyHash { get; set; } = string.Empty;

        /// <summary>
        /// Key prefix for identification (e.g., "pl_live_abc123")
        /// The first 8 chars of the key, stored for lookup
        /// </summary>
        [Required, MaxLength(16)]
        public string KeyPrefix { get; set; } = string.Empty;

        /// <summary>
        /// Descriptive name for this key
        /// </summary>
        [Required, MaxLength(128)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// User who owns this API key
        /// </summary>
        public Guid UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public User? User { get; set; }

        /// <summary>
        /// Is this key active?
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Allowed scopes (comma-separated: "analytics,substitutions")
        /// </summary>
        [MaxLength(256)]
        public string? Scopes { get; set; }

        /// <summary>
        /// Rate limit (requests per minute). 0 = unlimited.
        /// </summary>
        public int RateLimitPerMinute { get; set; } = 60;

        /// <summary>
        /// Optional expiration date
        /// </summary>
        public DateTime? ExpiresAt { get; set; }

        /// <summary>
        /// Last time this key was used
        /// </summary>
        public DateTime? LastUsedAt { get; set; }

        /// <summary>
        /// Total number of requests made with this key
        /// </summary>
        public long TotalRequests { get; set; } = 0;

        /// <summary>
        /// When the key was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Optional IP whitelist (comma-separated)
        /// </summary>
        [MaxLength(512)]
        public string? IpWhitelist { get; set; }

        /// <summary>
        /// When the key was revoked (null if still active)
        /// </summary>
        public DateTime? RevokedAt { get; set; }

        /// <summary>
        /// Reason for revocation
        /// </summary>
        [MaxLength(256)]
        public string? RevocationReason { get; set; }
    }

    /// <summary>
    /// Tracks API key usage for analytics and rate limiting.
    /// </summary>
    public class ApiKeyUsageLog
    {
        public long Id { get; set; }

        public Guid ApiKeyId { get; set; }

        [ForeignKey(nameof(ApiKeyId))]
        public ApiKey? ApiKey { get; set; }

        [MaxLength(64)]
        public string Endpoint { get; set; } = string.Empty;

        [MaxLength(10)]
        public string HttpMethod { get; set; } = string.Empty;

        public int StatusCode { get; set; }

        public int ResponseTimeMs { get; set; }

        [MaxLength(45)]
        public string? IpAddress { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
