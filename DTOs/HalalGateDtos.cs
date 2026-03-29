namespace Platerly.Api.DTOs
{
    /// <summary>
    /// Result of the Halal Gate evaluation for a recipe's ingredient list.
    /// </summary>
    public class HalalGateResult
    {
        /// <summary>
        /// True only when every ingredient passed. False means the recipe is discarded.
        /// </summary>
        public bool Passed { get; init; }

        /// <summary>
        /// One entry per ingredient that triggered a non-halal match.
        /// Empty when Passed is true.
        /// </summary>
        public List<HalalViolation> FailedIngredients { get; init; } = new();
    }

    /// <summary>
    /// Describes why a single ingredient failed the Halal Gate.
    /// </summary>
    public class HalalViolation
    {
        /// <summary>The ingredient string as it appeared in the recipe.</summary>
        public string IngredientName { get; init; } = string.Empty;

        /// <summary>The exact term that was matched (e.g. "bacon", "wine", "blood").</summary>
        public string MatchedTerm { get; init; } = string.Empty;

        /// <summary>"Pork" | "Alcohol" | "Blood"</summary>
        public string Category { get; init; } = string.Empty;

        /// <summary>Human-readable explanation of why this ingredient was rejected.</summary>
        public string Reason { get; init; } = string.Empty;
    }
}
