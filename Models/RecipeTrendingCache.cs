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
    }
}
