namespace Platerly.Api.DTOs
{
    // ── Request ────────────────────────────────────────────────────────────────

    public class IngredientPairingsRequest
    {
        /// <summary>
        /// The seed ingredient ID to find pairings for.
        /// Returns pairings where this ingredient is either IngredientA or IngredientB.
        /// </summary>
        public Guid? IngredientId { get; set; }

        /// <summary>Optional minimum affinity score (0-100, default 0).</summary>
        public decimal MinAffinityScore { get; set; } = 0;

        /// <summary>Optional minimum co-occurrence count (default 0).</summary>
        public int MinCoOccurrenceCount { get; set; } = 0;

        /// <summary>Optional region filter (returns pairings with matching TopRegion).</summary>
        public string? Region { get; set; }

        /// <summary>Optional cuisine type filter (e.g., "Italian", "Japanese").</summary>
        public string? Cuisine { get; set; }

        /// <summary>Number of results (default 50, max 200).</summary>
        public int Take { get; set; } = 50;

        /// <summary>Sort order: "affinity" (default), "cooccurrence".</summary>
        public string SortBy { get; set; } = "affinity";
    }

    // ── Response ───────────────────────────────────────────────────────────────

    public class IngredientPairingsResponse
    {
        /// <summary>
        /// The seed ingredient these pairings are for.
        /// Null when no specific ingredientId was requested.
        /// </summary>
        public IngredientSummaryDto? SeedIngredient { get; set; }

        /// <summary>Ingredient pairings ordered by affinity score descending.</summary>
        public List<B2BIngredientPairingDto> Pairings { get; set; } = new();

        public int TotalReturned => Pairings.Count;
    }

    public class IngredientSummaryDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Category { get; set; }
        public string? NickelClassification { get; set; }
    }

    public class B2BIngredientPairingDto
    {
        /// <summary>The paired ingredient (the "other" ingredient in the pair).</summary>
        public IngredientSummaryDto PairedIngredient { get; set; } = new();

        /// <summary>
        /// Number of recipes where both ingredients appear together.
        /// </summary>
        public int CoOccurrenceCount { get; set; }

        /// <summary>
        /// Affinity score 0–100: how often they appear together vs. in isolation.
        /// Higher = stronger culinary association.
        /// </summary>
        public decimal AffinityScore { get; set; }

        /// <summary>Region where this pairing is most common (null = global).</summary>
        public string? TopRegion { get; set; }

        /// <summary>Cuisine types where this pairing appears (e.g., "Italian,French").</summary>
        public string? CommonCuisines { get; set; }

        /// <summary>When this pairing record was last updated.</summary>
        public DateTime LastUpdated { get; set; }
    }
}
