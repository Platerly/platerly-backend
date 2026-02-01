using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
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
