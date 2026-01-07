using System;
using System.ComponentModel.DataAnnotations;

namespace Platerly.Api.Models
{
    public class BillingEmailEvent
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }

        [Required]
        [MaxLength(128)]
        public string StripeEventId { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string EventType { get; set; } = string.Empty;

        public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
    }
}
