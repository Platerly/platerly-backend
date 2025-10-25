using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Platerly.Api.Models
{
    public enum Difficulty
    {
        Easy = 0,
        Medium = 1,
        Hard = 2
    }

    [Index(nameof(CreatedAt), IsUnique = false)]
    [Index(nameof(ParentRecipeId), IsUnique = false)] // quick filter for remixes
    public class Recipe
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(160)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(4000)]
        public string Description { get; set; } = string.Empty;

        [MaxLength(12000)]
        public string Instructions { get; set; } = string.Empty;

        // Classification / discovery
        [MaxLength(64)]
        public string? Region { get; set; } // e.g., "Somali", "Turkish", etc.

        [MaxLength(512)]
        public string? ImageUrl { get; set; }

        [MaxLength(256)]
        public string? ImageBlobName { get; set; } // Blob storage name for the image

        // FAST PATH (Weeks 1–2): keep comma-separated tags for now ("vegan, low-nickel")
        // Normalize to Tag/RecipeTag in a later PR.
        public ICollection<RecipeTag> RecipeTags { get; set; } = new List<RecipeTag>();

        // Timing
        public int PrepTimeMinutes { get; set; } = 0;
        public int CookTimeMinutes { get; set; } = 0;

        // Difficulty: enum for robust filtering (nullable for a painless migration/backfill)
        public Difficulty? Difficulty { get; set; }

        // Remix lineage (null = original recipe)
        public Guid? ParentRecipeId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Soft delete + audit
        public bool IsDeleted { get; set; } = false;
        public DateTime? UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
        public Guid? DeletedByUserId { get; set; }

        // Optimistic concurrency
        [Timestamp]
        public byte[]? RowVersion { get; set; }

        // Foreign Key
        public Guid UserId { get; set; }

        // Navigation
        public User? User { get; set; }
        public ICollection<Ingredient>? Ingredients { get; set; }

        public ICollection<RecipeLike> Likes { get; set; } = new List<RecipeLike>();
        public ICollection<RecipeRepost> Reposts { get; set; } = new List<RecipeRepost>();

        // Convenience (not mapped): lets you check IsRemix in code without storing a bool
        [NotMapped]
        public bool IsRemix => ParentRecipeId.HasValue;
    }
}
