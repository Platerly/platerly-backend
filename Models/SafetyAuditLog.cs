using System.ComponentModel.DataAnnotations;

namespace Platerly.Api.Models
{
    /// <summary>
    /// Immutable audit record written for every call to a safety-sensitive endpoint
    /// (allergen scans, substitution lookups, nickel safety checks).
    /// Never deleted — retained for regulatory and safety review purposes.
    /// </summary>
    public class SafetyAuditLog
    {
        public long Id { get; set; }

        // ── Request identity ──────────────────────────────────────────
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        [MaxLength(10)]
        public string HttpMethod { get; set; } = string.Empty;

        [MaxLength(512)]
        public string Endpoint { get; set; } = string.Empty;

        // ── Caller identity ───────────────────────────────────────────
        /// <summary>Authenticated user ID, or null for anonymous/B2B-key callers.</summary>
        public Guid? UserId { get; set; }

        /// <summary>B2B API key ID, or null for user-session callers.</summary>
        public Guid? ApiKeyId { get; set; }

        [MaxLength(45)]
        public string? IpAddress { get; set; }

        [MaxLength(512)]
        public string? UserAgent { get; set; }

        // ── Response summary ──────────────────────────────────────────
        public int ResponseStatusCode { get; set; }
        public long ResponseTimeMs { get; set; }

        // ── Safety context ────────────────────────────────────────────
        /// <summary>Number of safety alerts generated in this response.</summary>
        public int AlertCount { get; set; }

        /// <summary>"FATAL" | "WARNING" | "INFO" | null (no alerts).</summary>
        [MaxLength(10)]
        public string? HighestAlertSeverity { get; set; }

        /// <summary>Comma-separated ingredient names queried (for audit trail).</summary>
        [MaxLength(1024)]
        public string? IngredientContext { get; set; }

        /// <summary>Comma-separated allergy names in scope for this call.</summary>
        [MaxLength(512)]
        public string? AllergyContext { get; set; }

        /// <summary>Confidence level returned ("HIGH" | "MEDIUM" | "LOW" | "UNKNOWN").</summary>
        [MaxLength(10)]
        public string? ConfidenceLevel { get; set; }

        // ── Error capture ─────────────────────────────────────────────
        /// <summary>Set when the response was an error (4xx/5xx).</summary>
        [MaxLength(2048)]
        public string? ErrorDetail { get; set; }
    }
}
