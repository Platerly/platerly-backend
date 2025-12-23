using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Allergy
{
    public interface IAllergyAlertService
    {
        Task<RecipeAlertsResponse> GetAlertsForRecipeAsync(Guid recipeId, Guid userId, CancellationToken ct = default);
    }
}
