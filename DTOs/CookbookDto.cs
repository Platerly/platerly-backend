using System;
using System.Collections.Generic;

namespace Platerly.Api.DTOs
{
    // -------- Create / Update --------
    public class CreateCookbookDto
    {
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsPublic { get; set; } = false;
    }

    public class UpdateCookbookDto
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public bool? IsPublic { get; set; }
    }

    // -------- Add recipe / reorder / note --------
    public class AddRecipeToCookbookDto
    {
        public Guid RecipeId { get; set; }
        public int? SortOrder { get; set; }   // if null, append
        public string? Note { get; set; }     // optional note for this entry
    }

    // Matches controller: Items[] with RecipeId + SortOrder
    public class ReorderCookbookDto
    {
        public List<Item> Items { get; set; } = new();

        public class Item
        {
            public Guid RecipeId { get; set; }
            public int SortOrder { get; set; }
        }
    }

    // Note updates are per-recipe inside the cookbook
    public class UpdateCookbookNoteDto
    {
        public Guid RecipeId { get; set; }
        public string? Note { get; set; }
    }

    // -------- Contribution requests --------
    public class CreateContributionRequestDto
    {
        public Guid RecipeId { get; set; }
        public string? Message { get; set; }
    }

    public class ContributionRequestDto
    {
        public Guid Id { get; set; }
        public Guid CookbookId { get; set; }
        public Guid RecipeId { get; set; }
        public Guid RequestedByUserId { get; set; }
        public string? Message { get; set; }
        public string Status { get; set; } = "Pending";
        public DateTime CreatedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
    }

    // -------- Read models --------
    public class CookbookSummaryDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsPublic { get; set; }
        public string? CoverImageUrl { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class CookbookDetailsDto : CookbookSummaryDto
    {
        public List<CookbookRecipeItemDto> Items { get; set; } = new();
    }

    public class CookbookRecipeItemDto
    {
        public Guid RecipeId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }

        public int SortOrder { get; set; }
        public string? Note { get; set; }
        public Guid AddedByUserId { get; set; }
        public DateTime AddedAt { get; set; }
    }
}
