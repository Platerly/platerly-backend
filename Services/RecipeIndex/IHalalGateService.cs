using Platerly.Api.DTOs;

namespace Platerly.Api.Services.RecipeIndex
{
    /// <summary>
    /// The Halal Gate — the most critical service in Phase 6.
    /// Every recipe MUST pass this gate before it is stored.
    /// Non-halal recipes are discarded permanently and never touch the database.
    /// </summary>
    public interface IHalalGateService
    {
        /// <summary>
        /// Evaluates a list of ingredient strings against non-halal detection rules.
        /// </summary>
        /// <param name="ingredients">Ingredient strings exactly as they appear in the source recipe.</param>
        /// <returns>
        /// A <see cref="HalalGateResult"/> where Passed=true means the recipe may be stored.
        /// Passed=false means the recipe must be discarded immediately.
        /// </returns>
        HalalGateResult Evaluate(IReadOnlyList<string> ingredients);
    }
}
