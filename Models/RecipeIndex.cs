using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Platerly.Api.Models
{
    /// <summary>
    /// An indexed recipe sourced from a legal external source.
    /// Every recipe in this table has already passed the Halal Gate before storage.
    /// Non-halal recipes are never stored — they are discarded at the gate.
    /// </summary>
    [Index(nameof(SourceDomain))]
    [Index(nameof(CuisineType))]
    [Index(nameof(IsAnalysed))]
    [Index(nameof(IndexedAt))]
    [Index(nameof(SourceUrl), IsUnique = true)]
    public class RecipeIndex
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(512)]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Canonical URL of the original recipe. Always displayed as attribution link.
        /// </summary>
        [Required, MaxLength(2048)]
        public string SourceUrl { get; set; } = string.Empty;

        [Required, MaxLength(256)]
        public string SourceDomain { get; set; } = string.Empty;

        /// <summary>
        /// Original author name from the source, if available.
        /// </summary>
        [MaxLength(256)]
        public string? OriginalAuthor { get; set; }

        /// <summary>
        /// JSON array of ingredient strings as they appear in the source (e.g. "200g chicken breast").
        /// </summary>
        public string IngredientsJson { get; set; } = "[]";

        /// <summary>
        /// JSON array of instruction steps.
        /// </summary>
        public string InstructionsJson { get; set; } = "[]";

        public DateTime IndexedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastCheckedAt { get; set; }

        // ── Pre-computed analysis results ───────────────────────────────────

        /// <summary>
        /// Full allergen flag results per ingredient. Populated by RecipeAnalysisPipelineService.
        /// </summary>
        public string? AllergenAnalysisJson { get; set; }

        /// <summary>
        /// Nickel load results: total load, threshold %, risk level, per-ingredient breakdown.
        /// Populated by RecipeAnalysisPipelineService.
        /// </summary>
        public string? NickelLoadJson { get; set; }

        /// <summary>
        /// Nutrition data: calories, macros, fibre, sodium.
        /// Populated by RecipeAnalysisPipelineService.
        /// </summary>
        public string? NutritionJson { get; set; }

        /// <summary>
        /// False until all three analysis JSONs have been populated by the pipeline.
        /// </summary>
        public bool IsAnalysed { get; set; } = false;

        /// <summary>
        /// Tracks which version of the analysis engine produced the current results.
        /// When the engine improves, unanalysed or older-version recipes can be re-queued.
        /// </summary>
        [MaxLength(32)]
        public string? AnalysisVersion { get; set; }

        // ── Source metadata ─────────────────────────────────────────────────

        /// <summary>
        /// Thumbnail URL from the original source. Only stored when the source
        /// explicitly permits image display. Never re-hosted by Recipzee.
        /// </summary>
        [MaxLength(2048)]
        public string? ThumbnailUrl { get; set; }

        [MaxLength(128)]
        public string? ServingSize { get; set; }

        public int? PrepTimeMinutes { get; set; }
        public int? CookTimeMinutes { get; set; }

        [MaxLength(128)]
        public string? CuisineType { get; set; }

        /// <summary>
        /// JSON array of tag strings (e.g. ["chicken", "rice", "gluten-free"]).
        /// </summary>
        public string? TagsJson { get; set; }

        /// <summary>
        /// How this recipe entered the index: "TheMealDB", "WebIndexed", "Manual", etc.
        /// </summary>
        [MaxLength(64)]
        public string? SubmissionSource { get; set; }

        /// <summary>
        /// When true the thumbnail is suppressed from display even though the URL is stored.
        /// Set manually if a blogger requests their image not be shown.
        /// </summary>
        public bool ImageDisplayBlocked { get; set; } = false;

        /// <summary>
        /// FK to the source this recipe was fetched from.
        /// </summary>
        public Guid SourceId { get; set; }
        public RecipeIndexSource? Source { get; set; }
    }
}
