using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Platerly.Api.Models
{
    /// <summary>
    /// Tracks recipe page views for trending algorithm
    /// </summary>
    [Table("RecipeViews")]
    public class RecipeView
    {
        [Key]
        public long Id { get; set; }

        public Guid RecipeId { get; set; }
        public Recipe? Recipe { get; set; }

        /// <summary>
        /// User who viewed (null for anonymous)
        /// </summary>
        public Guid? UserId { get; set; }
        public User? User { get; set; }

        /// <summary>
        /// SHA256 hash of IP for deduplication (privacy-preserving)
        /// </summary>
        [MaxLength(64)]
        public string? IpHash { get; set; }

        /// <summary>
        /// User agent for analytics
        /// </summary>
        [MaxLength(512)]
        public string? UserAgent { get; set; }

        /// <summary>
        /// Referrer URL
        /// </summary>
        [MaxLength(1024)]
        public string? Referrer { get; set; }

        public DateTime ViewedAt { get; set; } = DateTime.UtcNow;
    }
}
