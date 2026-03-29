using Platerly.Api.DTOs;

namespace Platerly.Api.Services.RecipeIndex
{
    /// <summary>
    /// Fetches recipes from legal external sources, runs every recipe through the
    /// Halal Gate, and stores only those that pass.
    /// </summary>
    public interface IRecipeIndexingService
    {
        /// <summary>
        /// Runs a full indexing pass against TheMealDB and the curated seed.
        /// Returns per-source statistics.
        /// </summary>
        Task<List<IndexingRunStats>> RunAsync(CancellationToken ct = default);
    }
}
