using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Platerly.Api.Models
{
    public class WebhookSubscription
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ApiKeyId { get; set; }

        [ForeignKey(nameof(ApiKeyId))]
        public ApiKey? ApiKey { get; set; }

        [Required, MaxLength(1024)]
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// Secret for HMAC-SHA256 signing of webhook payloads
        /// </summary>
        [Required, MaxLength(128)]
        public string Secret { get; set; } = string.Empty;

        /// <summary>
        /// Comma-separated event types (e.g., "ingredient.trending,dietary.shift")
        /// </summary>
        [MaxLength(512)]
        public string EventTypes { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastTriggeredAt { get; set; }
        public int ConsecutiveFailures { get; set; } = 0;
    }

    public class WebhookEvent
    {
        [Key]
        public long Id { get; set; }

        public Guid SubscriptionId { get; set; }

        [ForeignKey(nameof(SubscriptionId))]
        public WebhookSubscription? Subscription { get; set; }

        [Required, MaxLength(64)]
        public string EventType { get; set; } = string.Empty;

        /// <summary>
        /// JSON payload
        /// </summary>
        public string Payload { get; set; } = "{}";

        public WebhookEventStatus Status { get; set; } = WebhookEventStatus.Pending;

        public int Attempts { get; set; } = 0;
        public DateTime? NextRetryAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? SentAt { get; set; }
        [MaxLength(1024)]
        public string? LastError { get; set; }
    }

    public enum WebhookEventStatus
    {
        Pending = 0,
        Sent = 1,
        Failed = 2
    }
}
