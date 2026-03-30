namespace Platerly.Api.Services.RecipeIndex
{
    public record RobotsCheckResult(bool Allowed, string? DisallowNote);

    public interface IRobotsTxtService
    {
        /// <summary>
        /// Fetches and parses robots.txt for the given domain.
        /// Checks for Recipzee-Bot user agent rules first, then falls back to wildcard (*) rules.
        /// </summary>
        Task<RobotsCheckResult> CheckAsync(string domain, CancellationToken ct = default);
    }
}
