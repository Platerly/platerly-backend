using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Allergy
{
    /// <summary>Provides allergen alert detection for recipes against a user's allergen profile.</summary>
    public interface IAllergyAlertService
    {
        Task<RecipeAlertsResponse> GetAlertsForRecipeAsync(Guid recipeId, Guid userId, CancellationToken ct = default);
    }
}
