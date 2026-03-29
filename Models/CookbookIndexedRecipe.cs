using System.ComponentModel.DataAnnotations;

namespace Platerly.Api.Models
{
    /// <summary>
    /// Links a Cookbook to an indexed recipe from the RecipeIndex table.
    /// Separate from CookbookRecipe (which links to user-created recipes).
    ///
    /// Source attribution (SourceUrl, SourceDomain) is always preserved
    /// on the RecipeIndex row — even inside a personal cookbook the
    /// original source remains permanently visible.
    ///
    /// Free tier: max 3 indexed recipe saves per user (enforced by endpoint).
    /// Premium / trial: unlimited.
    /// </summary>
    public class CookbookIndexedRecipe
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid CookbookId { get; set; }
        public Cookbook Cookbook { get; set; } = null!;

        public Guid RecipeIndexId { get; set; }
        public RecipeIndex RecipeIndex { get; set; } = null!;

        /// <summary>
        /// Snapshot of the allergen analysis at the time of saving.
        /// Preserved so the user's analysis doesn't change if the pipeline re-runs.
        /// </summary>
        public string? AllergenSnapshotJson { get; set; }

        public Guid SavedByUserId { get; set; }
        public User? SavedByUser { get; set; }

        public DateTime SavedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(512)]
        public string? Note { get; set; }
    }
}
