using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Nutrition
{
    /// <summary>Identifies nutrient deficiency risks from a multi-allergen avoidance profile and returns allergen-safe compensating food recommendations.</summary>
    public interface INutritionGapService
    {
        Task<NutritionGapResult> AnalyseGapsAsync(
            NutritionGapRequest request,
            CancellationToken ct = default);
    }
}
