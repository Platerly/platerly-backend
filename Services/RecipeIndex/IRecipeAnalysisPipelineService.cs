using Platerly.Api.DTOs;

namespace Platerly.Api.Services.RecipeIndex
{
    /// <summary>
    /// Processes recipes in the RecipeIndex that have IsAnalysed=false,
    /// running allergen intelligence and nickel load analysis on each one.
    /// </summary>
    public interface IRecipeAnalysisPipelineService
    {
        /// <summary>
        /// Processes all unanalysed recipes in batches until none remain.
        /// Returns statistics for the completed run.
        /// </summary>
        Task<AnalysisPipelineStats> RunAsync(CancellationToken ct = default);
    }
}
