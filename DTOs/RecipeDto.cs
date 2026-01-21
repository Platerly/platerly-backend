// RecipeDto.cs
namespace Platerly.Api.DTOs
{
    public class RecipeDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Slug { get; set; }
        public string? UrlPath { get; set; } // e.g., "/johndoe/chocolate-chip-cookies"
        public string? Description { get; set; }
        public string? Instructions { get; set; }
        public string? Region { get; set; }
        public string? ImageUrl { get; set; }
        public string? PhotoCredit { get; set; } // e.g., "John Smith"
        public string? PhotoCreditUrl { get; set; } // e.g., "https://www.pexels.com/@johnsmith"
        public DateTime CreatedAt { get; set; }

        public List<IngredientDto> Ingredients { get; set; } = new();
        public List<string> IngredientNames { get; set; } = new();

        public string AuthorUsername { get; set; } = string.Empty;

        // ✅ Make this consistent with RecipeFeedDto
        //public string Tags { get; set; } = string.Empty; // legacy
        public List<string> Tags { get; set; } = new();
        public int? PrepTimeMinutes { get; set; }
        public int? CookTimeMinutes { get; set; }
        public string? Difficulty { get; set; }
        public string? Type { get; set; } // Meal, Shake, Dessert, etc.

        // ✅ Match feed for color/severity rendering
        public List<string> MatchedAllergenTerms { get; set; } = new();
        
        // key = ingredient (lowercased), value = "high" | "medium" | "low"
        public Dictionary<string, string> MatchedAllergenSeverity { get; set; } = new();
    }
}
