using Platerly.Api.DTOs;

namespace Platerly.Api.DTOs
{
    public class RecipeFeedDto
    {
        public Guid RecipeId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Slug { get; set; } // SEO-friendly URL slug
        public string Description { get; set; } = string.Empty;
        public string? Region { get; set; }
        public string? ImageUrl { get; set; }
        public string? PhotoCredit { get; set; }
        public string? PhotoCreditUrl { get; set; }
        public bool IsRemix { get; set; }
        public bool IsLikedByCurrentUser { get; set; }
        public int Likes { get; set; }
        public DateTime CreatedAt { get; set; }
        public string AuthorUsername { get; set; } = string.Empty;

        public List<IngredientDto> Ingredients { get; set; } = new();
        public PublicUserDto PostedBy { get; set; } = new();
        public string Type { get; set; } = "original";
        public bool IsRepostedByCurrentUser { get; set; }
        public int RepostCount { get; set; }
        public bool HasRemixedByCurrentUser { get; set; }
        public int RemixCount { get; set; }
        public List<string> IngredientNames { get; set; } = new();
        public bool IsFollowing { get; set; }

        public int? PrepTimeMinutes { get; set; }
        public int? CookTimeMinutes { get; set; }
        public string? Difficulty { get; set; }
        public string? RecipeType { get; set; } // Meal, Shake, Dessert, etc.
        public List<string> Tags { get; set; } = new();

        // already used for list display
        public List<string> MatchedAllergenTerms { get; set; } = new();

        // ✅ NEW: key = ingredient (lowercased), value = "high" | "medium" | "low"
        public Dictionary<string, string> MatchedAllergenSeverity { get; set; } = new();
    }
}
