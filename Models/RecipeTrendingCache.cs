using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Platerly.Api.Models
{
    [Table("RecipeTrendingCache")]
    public class RecipeTrendingCache
    {
        [Key]
        public Guid RecipeId { get; set; }    // PK (and FK to Recipes if you want to enforce)
        public double Score { get; set; }     // computed trending score
        public int Rank { get; set; }         // 1 = top
        public DateTime ComputedAt { get; set; } = DateTime.UtcNow;

        // ---- Extended fields for enhanced trending ----

        /// <summary>
        /// Previous rank for delta calculation (null if new to trending)
        /// </summary>
        public int? PreviousRank { get; set; }

        /// <summary>
        /// Rank change since last computation (positive = rising, negative = falling)
        /// </summary>
        public int RankDelta { get; set; }

        /// <summary>
        /// Velocity multiplier based on engagement acceleration
        /// </summary>
        public double VelocityScore { get; set; } = 1.0;

        /// <summary>
        /// View count in the scoring window
        /// </summary>
        public int ViewCount { get; set; }

        /// <summary>
        /// Comment count in the scoring window
        /// </summary>
        public int CommentCount { get; set; }

        /// <summary>
        /// Cookbook save count in the scoring window
        /// </summary>
        public int SaveCount { get; set; }

        /// <summary>
        /// Like count in the scoring window
        /// </summary>
        public int LikeCount { get; set; }

        /// <summary>
        /// Repost count in the scoring window
        /// </summary>
        public int RepostCount { get; set; }

        /// <summary>
        /// Remix count in the scoring window
        /// </summary>
        public int RemixCount { get; set; }
    }
}
