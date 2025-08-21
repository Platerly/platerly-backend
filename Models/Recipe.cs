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
        public string? ImageBlobName { get; set; } // Blob storage name for the image
        public string Tags { get; set; } = string.Empty; // Comma-separated tags, "vegan, low-nickel"

        public int PrepTimeMinutes { get; set; } = 0; // Preparation time in minutes
        public int CookTimeMinutes { get; set; } = 0; // Cooking time in minutes
        public string Difficulty { get; set; } = string.Empty; // e.g., "Easy", "Medium", "Hard"

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Soft delete + audit + edits
        public bool IsDeleted { get; set; } = false;
        public DateTime? UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
        public Guid? DeletedByUserId { get; set; }

        // Optimistic concurrency
        public byte[]? RowVersion { get; set; }

        // Foreign Key
        public Guid UserId { get; set; }

        // Navigation
        public User? User { get; set; }
        public ICollection<Ingredient>? Ingredients { get; set; }

        public ICollection<RecipeLike> Likes { get; set; } = new List<RecipeLike>();

        public ICollection<RecipeRepost> Reposts { get; set; } = new List<RecipeRepost>();

    }
}
