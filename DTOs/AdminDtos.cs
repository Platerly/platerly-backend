using System.ComponentModel.DataAnnotations;
using Platerly.Api.Models;

namespace Platerly.Api.DTOs
{
    // ========== Prohibited Ingredients Admin ==========

    public class ProhibitedIngredientDto
    {
        public int Id { get; set; }
        public string Keyword { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty; // ProhibitedCategory as string
        public string MatchType { get; set; } = string.Empty; // MatchType as string
        public string? Reason { get; set; }
        public string? AdminNotes { get; set; }
        public string Severity { get; set; } = string.Empty; // ViolationSeverity as string
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid? CreatedByUserId { get; set; }
    }

    public class CreateProhibitedIngredientDto
    {
        [Required, MaxLength(256)]
        public string Keyword { get; set; } = string.Empty;

        [Required]
        public string Category { get; set; } = "Haram"; // Haram, Dangerous, Illegal, Harmful, Offensive

        public string MatchType { get; set; } = "Contains"; // Exact, Contains, StartsWith, Regex

        [MaxLength(512)]
        public string? Reason { get; set; }

        [MaxLength(1024)]
        public string? AdminNotes { get; set; }

        public string Severity { get; set; } = "Block"; // Warn, Block, BlockAndFlag, BlockAndSuspend
    }

    public class UpdateProhibitedIngredientDto
    {
        [MaxLength(256)]
        public string? Keyword { get; set; }

        public string? Category { get; set; }

        public string? MatchType { get; set; }

        [MaxLength(512)]
        public string? Reason { get; set; }

        [MaxLength(1024)]
        public string? AdminNotes { get; set; }

        public string? Severity { get; set; }

        public bool? IsActive { get; set; }
    }

    public class ProhibitedIngredientListResponse
    {
        public List<ProhibitedIngredientDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public Dictionary<string, int> CategoryCounts { get; set; } = new();
    }

    public class BulkCreateProhibitedDto
    {
        [Required]
        public List<CreateProhibitedIngredientDto> Items { get; set; } = new();
    }

    public class BulkCreateResult
    {
        public int Created { get; set; }
        public int Skipped { get; set; }
        public List<string> SkippedKeywords { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    // ========== Ingredient Category Admin ==========

    public class IngredientCategoryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int? ParentCategoryId { get; set; }
        public string? ParentCategoryName { get; set; }
        public string? IconName { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public string? CommonAllergens { get; set; }
        public string? DietaryFlags { get; set; }
        public int ChildCount { get; set; }
        public int IngredientCount { get; set; }
    }

    public class CreateIngredientCategoryDto
    {
        [Required, MaxLength(128)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(128)]
        public string Slug { get; set; } = string.Empty;

        [MaxLength(512)]
        public string? Description { get; set; }

        public int? ParentCategoryId { get; set; }

        [MaxLength(64)]
        public string? IconName { get; set; }

        public int SortOrder { get; set; } = 0;

        [MaxLength(256)]
        public string? CommonAllergens { get; set; }

        [MaxLength(256)]
        public string? DietaryFlags { get; set; }
    }

    public class UpdateIngredientCategoryDto
    {
        [MaxLength(128)]
        public string? Name { get; set; }

        [MaxLength(128)]
        public string? Slug { get; set; }

        [MaxLength(512)]
        public string? Description { get; set; }

        public int? ParentCategoryId { get; set; }

        [MaxLength(64)]
        public string? IconName { get; set; }

        public int? SortOrder { get; set; }

        public bool? IsActive { get; set; }

        [MaxLength(256)]
        public string? CommonAllergens { get; set; }

        [MaxLength(256)]
        public string? DietaryFlags { get; set; }
    }

    // ========== Global Ingredient Admin ==========

    public class GlobalIngredientAdminDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string NormalizedName { get; set; } = string.Empty;
        public string? Aliases { get; set; }
        public bool IsAllergenic { get; set; }
        public int? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public long TotalUsageCount { get; set; }
        public int UniqueRecipeCount { get; set; }
        public int? PopularityRank { get; set; }
        public string? PrimaryRegion { get; set; }
        public string? DietaryFlags { get; set; }
        public bool IsVerified { get; set; }
        public bool IsActive { get; set; }
        public Guid? MergedIntoId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class UpdateGlobalIngredientDto
    {
        [MaxLength(256)]
        public string? Name { get; set; }

        [MaxLength(512)]
        public string? Aliases { get; set; }

        public bool? IsAllergenic { get; set; }

        public int? CategoryId { get; set; }

        [MaxLength(64)]
        public string? PrimaryRegion { get; set; }

        [MaxLength(256)]
        public string? DietaryFlags { get; set; }

        [MaxLength(64)]
        public string? NutritionalCategory { get; set; }

        public bool? IsVerified { get; set; }

        public bool? IsActive { get; set; }

        // ── Nickel fields ──────────────────────────────────────────────────────

        public decimal? NickelMicrogramsPerServing { get; set; }

        public int? NickelServingSizeGrams { get; set; }

        public NickelClassification? NickelClassification { get; set; }

        public NickelConfidenceLevel? NickelConfidenceLevel { get; set; }

        public decimal? NickelStandardDeviation { get; set; }

        public int? NickelDataPointCount { get; set; }

        public bool? NickelHighVariability { get; set; }

        [MaxLength(512)]
        public string? NickelPreparationNote { get; set; }

        [MaxLength(512)]
        public string? NickelDataSource { get; set; }

        public int? NickelDataSourceYear { get; set; }
    }

    public class MergeIngredientsDto
    {
        [Required]
        public Guid SourceIngredientId { get; set; }

        [Required]
        public Guid TargetIngredientId { get; set; }
    }
}
