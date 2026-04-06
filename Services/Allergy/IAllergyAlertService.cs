using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Allergy
{
    /// <summary>Provides allergen alert detection for recipes against a user's allergen profile.</summary>
    public interface IAllergyAlertService
    {
        Task<RecipeAlertsResponse> GetAlertsForRecipeAsync(Guid recipeId, Guid userId, CancellationToken ct = default);

        /// <summary>
        /// Batch variant — resolves alerts for multiple recipes in 4-5 total DB queries regardless
        /// of page size. Returns the same <see cref="RecipeAlertsResponse"/> structure per recipe as
        /// the single-recipe overload; recipes with no alerts are included with an empty Alerts list.
        /// </summary>
        Task<Dictionary<Guid, RecipeAlertsResponse>> GetAlertsForRecipesAsync(
            IReadOnlyList<Guid> recipeIds,
            Guid userId,
            CancellationToken ct = default);

        /// <summary>
        /// Expands a list of user allergen keywords into a flat list of matched terms using the
        /// allergen resolver, filtered by <paramref name="minSeverityOrEmpty"/> when supplied.
        /// Falls back to raw keyword list if expansion fails.
        /// </summary>
        List<string> ExpandViewerKeywords(IReadOnlyList<string> userKeywords, string minSeverityOrEmpty);
    }
}
