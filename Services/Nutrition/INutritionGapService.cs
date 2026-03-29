using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Nutrition
{
    public interface INutritionGapService
    {
        Task<NutritionGapResult> AnalyseGapsAsync(
            NutritionGapRequest request,
            CancellationToken ct = default);
    }
}
