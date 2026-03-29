using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Platerly.Api.DTOs
{
    // -------- Create / Update --------
    public class CreateCookbookDto
    {
        [Required(ErrorMessage = "Title is required")]
        [StringLength(128, MinimumLength = 1, ErrorMessage = "Title must be between 1 and 128 characters")]
        public string Title { get; set; } = string.Empty;

        [StringLength(1024, ErrorMessage = "Description cannot exceed 1024 characters")]
        public string? Description { get; set; }

        public bool IsPublic { get; set; } = false;
    }

    public class UpdateCookbookDto
    {
        [StringLength(128, MinimumLength = 1, ErrorMessage = "Title must be between 1 and 128 characters")]
        public string? Title { get; set; }

        [StringLength(1024, ErrorMessage = "Description cannot exceed 1024 characters")]
        public string? Description { get; set; }

        public bool? IsPublic { get; set; }
    }

    // -------- Add recipe / reorder / note --------
    public class AddRecipeToCookbookDto
    {
        [Required(ErrorMessage = "Recipe ID is required")]
        public Guid RecipeId { get; set; }

        public int? SortOrder { get; set; }   // if null, append

        [StringLength(512, ErrorMessage = "Note cannot exceed 512 characters")]
        public string? Note { get; set; }     // optional note for this entry
    }

    public class AddIndexedRecipeToCookbookDto
    {
        [Required(ErrorMessage = "RecipeIndexId is required")]
        public Guid RecipeIndexId { get; set; }

        [StringLength(512)]
        public string? Note { get; set; }
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
        [Required(ErrorMessage = "Recipe ID is required")]
        public Guid RecipeId { get; set; }

        [StringLength(512, ErrorMessage = "Note cannot exceed 512 characters")]
        public string? Note { get; set; }
    }

    // -------- Contribution requests --------
    public class CreateContributionRequestDto
    {
        [Required(ErrorMessage = "Recipe ID is required")]
        public Guid RecipeId { get; set; }

        [StringLength(1024, ErrorMessage = "Message cannot exceed 1024 characters")]
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
        public int RecipeCount { get; set; }
        public string? OwnerUsername { get; set; }
    }

    public class CookbookDetailsDto : CookbookSummaryDto
    {
        public List<CookbookRecipeItemDto> Items { get; set; } = new();

        /// <summary>
        /// Indexed (externally-sourced, halal-verified) recipes saved to this cookbook.
        /// Displayed alongside user-created recipes with a "Saved from {sourceDomain}" badge.
        /// </summary>
        public List<CookbookIndexedRecipeItemDto> IndexedItems { get; set; } = new();
    }

    public class CookbookIndexedRecipeItemDto
    {
        public Guid    RecipeIndexId  { get; set; }
        public string  Title          { get; set; } = string.Empty;
        public string? ThumbnailUrl   { get; set; }
        public string  SourceDomain   { get; set; } = string.Empty;
        public string  SourceUrl      { get; set; } = string.Empty;
        public string? CuisineType    { get; set; }
        public string  NickelRiskLevel { get; set; } = "Unknown";
        public string? AllergenSnapshotJson { get; set; }
        public string? Note           { get; set; }
        public DateTime SavedAt       { get; set; }
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
        public string? AddedByUsername { get; set; }
        public DateTime AddedAt { get; set; }
    }
}
