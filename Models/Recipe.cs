using System;

namespace Platerly.Api.Models
{
    public class Recipe
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Instructions { get; set; } = string.Empty;

        public string? Region { get; set; } // e.g., "Somali", "Turkish", etc.
        public string? ImageUrl { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Foreign Key
        public Guid UserId { get; set; }

        // Navigation
        public User? User { get; set; }
        public ICollection<Ingredient>? Ingredients { get; set; }

        public ICollection<RecipeLike> Likes { get; set; } = new List<RecipeLike>();

        public ICollection<RecipeRepost> Reposts { get; set; } = new List<RecipeRepost>();

    }
}
