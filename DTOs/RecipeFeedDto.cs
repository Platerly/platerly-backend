using Platerly.Api.DTOs;

namespace Platerly.Api.DTOs
{
    public class RecipeFeedDto
    {
        public Guid RecipeId { get; set; }  // <-- this line was previously RecipeId
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? Region { get; set; }
        public string? ImageUrl { get; set; }
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
        public bool IsFollowing { get; set; } // New field to indicate if current user follows recipe author

        public int? PrepTimeMinutes { get; set; }
        public int? CookTimeMinutes { get; set; }
        public string? Difficulty { get; set; }
        public List<string> Tags { get; set; } = new();



    }
}
