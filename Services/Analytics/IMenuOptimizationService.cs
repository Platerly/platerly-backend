using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    /// <summary>Provides B2B menu analysis and optimization suggestions based on allergen coverage, dietary diversity, and ingredient performance.</summary>
    public interface IMenuOptimizationService
    {
        Task<MenuAnalysisResponse> AnalyzeMenuAsync(
            MenuAnalysisRequest request,
            CancellationToken ct = default);

        Task<MenuSuggestionsResponse> GetSuggestionsAsync(
            MenuSuggestionsRequest request,
            CancellationToken ct = default);
    }
}
