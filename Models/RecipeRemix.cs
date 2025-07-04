using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Platerly.Api.Models
{
    public class RecipeRemix
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid RemixRecipeId { get; set; }  // The new remix
        [ForeignKey(nameof(RemixRecipeId))]
        public Recipe RemixRecipe { get; set; } = null!;

        public Guid? OriginalRecipeId { get; set; }  // The original recipe it remixed
        [ForeignKey(nameof(OriginalRecipeId))]
        public Recipe? OriginalRecipe { get; set; }

        public Guid? ParentRecipeId { get; set; }
        [ForeignKey(nameof(ParentRecipeId))]
        public Recipe? ParentRecipe { get; set; }

        public DateTime RemixedAt { get; set; } = DateTime.UtcNow;

        
    }
}
