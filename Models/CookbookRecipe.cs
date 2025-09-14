using System;

namespace Platerly.Api.Models
{
    public class CookbookRecipe
    {
        public Guid CookbookId { get; set; }
        public Cookbook Cookbook { get; set; } = null!;

        public Guid RecipeId { get; set; }
        public Recipe Recipe { get; set; } = null!;

        // NEW
        public int SortOrder { get; set; } = 0;         // per-cookbook ordering
        public string? Note { get; set; }               // why it belongs, tips, etc.

        public Guid AddedByUserId { get; set; }         // audit
        public User? AddedByUser { get; set; }

        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    }
}
