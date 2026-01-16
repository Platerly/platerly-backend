using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Platerly.Api.Models
{
    /// <summary>
    /// Cache for trending creators/chefs
    /// </summary>
    [Table("UserTrendingCache")]
    public class UserTrendingCache
    {
        [Key]
        public Guid UserId { get; set; }
        public User? User { get; set; }

        /// <summary>
        /// Computed trending score for this creator
        /// </summary>
        public double Score { get; set; }

        /// <summary>
        /// Rank among all creators (1 = top)
        /// </summary>
        public int Rank { get; set; }

        /// <summary>
        /// Previous rank for delta calculation
        /// </summary>
        public int? PreviousRank { get; set; }

        /// <summary>
        /// Rank change (positive = rising, negative = falling)
        /// </summary>
        public int RankDelta { get; set; }

        /// <summary>
        /// New followers gained in the scoring window
        /// </summary>
        public int FollowerGrowth { get; set; }

        /// <summary>
        /// Total engagement across all recipes in scoring window
        /// </summary>
        public int RecipeEngagementTotal { get; set; }

        /// <summary>
        /// Number of trending recipes this creator has
        /// </summary>
        public int TrendingRecipeCount { get; set; }

        public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
    }
}
