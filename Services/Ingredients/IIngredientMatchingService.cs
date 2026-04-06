using Platerly.Api.Models;

namespace Platerly.Api.Services.Ingredients
{
    /// <summary>
    /// Resolves free-text ingredient names to <see cref="GlobalIngredient"/> rows using a
    /// three-level fallback chain:
    /// <list type="number">
    ///   <item>Exact match on <c>GlobalIngredient.NormalizedName</c>.</item>
    ///   <item>Alias match on <c>GlobalIngredientAlias.NormalizedAlias</c> (SQL index lookup).</item>
    ///   <item>Progressive word-drop fuzzy match on <c>GlobalIngredient.NormalizedName</c>
    ///     (e.g., "extra-lean ground beef" → "ground beef" → "beef").</item>
    /// </list>
    /// </summary>
    public interface IIngredientMatchingService
    {
        /// <summary>
        /// Returns a dictionary keyed by each input <paramref name="normalizedNames"/> entry
        /// that could be resolved. Unresolvable names are simply absent from the result.
        /// </summary>
        Task<Dictionary<string, GlobalIngredient>> ResolveAsync(
            IEnumerable<string> normalizedNames,
            CancellationToken ct = default);
    }
}
