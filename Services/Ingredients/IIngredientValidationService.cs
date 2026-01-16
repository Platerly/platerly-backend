namespace Platerly.Api.Services.Ingredients
{
    /// <summary>
    /// Result of ingredient validation
    /// </summary>
    public class IngredientValidationResult
    {
        public bool IsValid { get; set; }
        public List<IngredientViolation> Violations { get; set; } = new();

        public static IngredientValidationResult Success() => new() { IsValid = true };

        public static IngredientValidationResult Failed(List<IngredientViolation> violations) => new()
        {
            IsValid = false,
            Violations = violations
        };
    }

    public class IngredientViolation
    {
        public string IngredientName { get; set; } = string.Empty;
        public string MatchedKeyword { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
    }

    /// <summary>
    /// Service for validating ingredients against prohibited items list.
    /// Enforces halal requirements and blocks dangerous/illegal ingredients.
    /// </summary>
    public interface IIngredientValidationService
    {
        /// <summary>
        /// Validates a list of ingredient names against prohibited ingredients.
        /// </summary>
        /// <param name="ingredientNames">List of ingredient names to validate</param>
        /// <returns>Validation result with any violations found</returns>
        Task<IngredientValidationResult> ValidateIngredientsAsync(
            IEnumerable<string> ingredientNames,
            CancellationToken ct = default);

        /// <summary>
        /// Validates a single ingredient name.
        /// </summary>
        Task<IngredientValidationResult> ValidateIngredientAsync(
            string ingredientName,
            CancellationToken ct = default);

        /// <summary>
        /// Checks if an ingredient name matches any prohibited keyword.
        /// Returns the matched prohibited ingredient if found.
        /// </summary>
        Task<Models.ProhibitedIngredient?> FindProhibitedMatchAsync(
            string ingredientName,
            CancellationToken ct = default);

        /// <summary>
        /// Reloads the prohibited ingredients cache from the database.
        /// Call this after adding/updating prohibited ingredients.
        /// </summary>
        Task RefreshCacheAsync(CancellationToken ct = default);
    }
}
