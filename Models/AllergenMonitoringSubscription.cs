using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Platerly.Api.Models
{
    /// <summary>
    /// Per-customer allergen change monitoring subscription.
    ///
    /// Each subscription watches a list of ingredients and fires a webhook
    /// (via the linked WebhookSubscription) whenever the allergen profile
    /// of any monitored ingredient changes in the GlobalIngredientAllergens table.
    ///
    /// Change detection: SHA-256 of a canonically-sorted JSON allergen snapshot
    /// is recomputed by AllergenMonitoringBackgroundService and compared against
    /// the stored SnapshotHash. On mismatch a WebhookEvent is created for delivery
    /// by the existing WebhookDispatchService.
    /// </summary>
    public class AllergenMonitoringSubscription
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        // ── Ownership ──────────────────────────────────────────────────────────

        public Guid ApiKeyId { get; set; }

        [ForeignKey(nameof(ApiKeyId))]
        public ApiKey? ApiKey { get; set; }

        /// <summary>
        /// The WebhookSubscription used for event delivery (event type: allergen.change).
        /// Created at registration time; deleted when this subscription is deleted.
        /// </summary>
        public Guid WebhookSubscriptionId { get; set; }

        [ForeignKey(nameof(WebhookSubscriptionId))]
        public WebhookSubscription? WebhookSubscription { get; set; }

        // ── Monitoring scope ──────────────────────────────────────────────────

        /// <summary>
        /// Comma-separated ingredient names to monitor (as supplied by the customer).
        /// Stored as-submitted; normalised at check time.
        /// </summary>
        [Required, MaxLength(4096)]
        public string IngredientNames { get; set; } = string.Empty;

        /// <summary>
        /// Optional comma-separated allergen type filter.
        /// When set, only changes to these allergen types trigger a notification.
        /// Null / empty = notify on any allergen change.
        /// </summary>
        [MaxLength(512)]
        public string? AllergenTypesFilter { get; set; }

        // ── Change-detection state ─────────────────────────────────────────────

        /// <summary>
        /// SHA-256 hex string of the canonically-sorted allergen snapshot at
        /// the last successful check (or at registration time).
        /// </summary>
        [MaxLength(64)]
        public string SnapshotHash { get; set; } = string.Empty;

        /// <summary>
        /// Full JSON of the allergen snapshot — stored so the background service
        /// can compute a human-readable diff for the webhook payload without an
        /// extra DB query against the historical state.
        /// </summary>
        public string SnapshotJson { get; set; } = "[]";

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastCheckedAt { get; set; }
        public DateTime? LastChangeDetectedAt { get; set; }
        public int TotalChangesDetected { get; set; } = 0;
    }
}
