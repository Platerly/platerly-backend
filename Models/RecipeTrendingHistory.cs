using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Platerly.Api.Models
{
    /// <summary>
    /// Stores historical trending data for tracking rank changes over time
    /// </summary>
    [Table("RecipeTrendingHistory")]
    public class RecipeTrendingHistory
    {
        [Key]
        public long Id { get; set; }

        public Guid RecipeId { get; set; }
        public Recipe? Recipe { get; set; }

        public double Score { get; set; }
        public int Rank { get; set; }

        /// <summary>
        /// When this snapshot was computed
        /// </summary>
        public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
    }
}
