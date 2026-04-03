namespace Platerly.Api.Services.RecipeIndex
{
    public record GeneralIndexingStats(
        string Domain,
        int UrlsDiscovered,
        int RecipesFound,
        int PassedHalalGate,
        int FailedHalalGate,
        int AlreadyIndexed,
        int Errors,
        TimeSpan Elapsed
    );

    /// <summary>Crawls food blogs and recipe websites for halal recipes, honouring robots.txt and per-domain caps (<c>MaxUrlsPerDomain=250</c>, <c>MaxRecipesPerDomain=100</c>).</summary>
    public interface IGeneralRecipeIndexingService
    {
        /// <summary>
        /// Indexes all active, allowed sources from the database.
        /// Checks blocked domains and robots.txt before touching any domain.
        /// </summary>
        Task<IReadOnlyList<GeneralIndexingStats>> RunAllAsync(CancellationToken ct = default);

        /// <summary>
        /// Indexes a single source by domain name.
        /// </summary>
        Task<GeneralIndexingStats> RunForDomainAsync(string domain, CancellationToken ct = default);
    }
}
