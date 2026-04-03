using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platerly.Api.Data;
using Platerly.Api.Models;

namespace Platerly.Api.Services.RecipeIndex
{
    /// <summary>
    /// Seeds the initial list of domains into RecipeIndexSources.
    /// Safe to call multiple times — skips existing domains.
    /// robots.txt is checked lazily by the indexer on first crawl attempt.
    /// </summary>
    public class RecipeIndexSourceSeed
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<RecipeIndexSourceSeed> _log;

        public RecipeIndexSourceSeed(PlaterlyDbContext db, ILogger<RecipeIndexSourceSeed> log)
        {
            _db = db;
            _log = log;
        }

        private static readonly List<SeedEntry> Seeds = new()
        {
            // TheMealDB is handled by RecipeIndexingService (official API) — keep active, no web crawl
            new("TheMealDB",                "themealdb.com",              null,
                "Official public API. Recipes are openly licensed for non-commercial use. No crawling needed.",
                RequestDelayMs: 500, AllowsThumbnail: true),

            new("AllRecipes",               "allrecipes.com",             "https://allrecipes.com/sitemap.xml",
                "Large public recipe database. Ingredient lists are facts, not copyrightable."),

            new("BBC Good Food",            "bbcgoodfood.com",            "https://www.bbcgoodfood.com/sitemap.xml",
                "UK recipe database. Publicly available ingredient lists used for allergen analysis."),

            new("Simply Recipes",           "simplyrecipes.com",          "https://www.simplyrecipes.com/sitemap_index.xml",
                "Well-structured recipe site. JSON-LD structured data available."),

            new("Minimalist Baker",         "minimalistbaker.com",        "https://minimalistbaker.com/sitemap_index.xml",
                "Allergy-friendly food blog. Ingredient lists used for allergen analysis."),

            new("The Mediterranean Dish",   "themediterraneandish.com",   "https://www.themediterraneandish.com/sitemap_index.xml",
                "Mediterranean and halal-friendly recipes. Ingredient lists used for analysis."),

            new("Half Baked Harvest",       "halfbakedharvest.com",       "https://www.halfbakedharvest.com/sitemap_index.xml",
                "Popular recipe blog. Ingredient lists used for allergen analysis."),

            new("Feel Good Foodie",         "feelgoodfoodie.net",         "https://feelgoodfoodie.net/sitemap_index.xml",
                "Halal and Middle Eastern recipes specifically. Ingredient lists used for allergen analysis.",
                AllowsThumbnail: true),

            new("Ambitious Kitchen",        "ambitiouskitchen.com",       "https://www.ambitiouskitchen.com/sitemap_index.xml",
                "Allergy-friendly recipes. Ingredient lists used for analysis."),

            new("Skinnytaste",              "skinnytaste.com",            "https://www.skinnytaste.com/sitemap_index.xml",
                "Healthy recipes with structured data. Ingredient lists used for allergen analysis."),
        };

        /// <summary>
        /// Inserts any missing domains from the embedded <c>Seeds</c> list into <c>RecipeIndexSources</c>.
        /// Existing domains (matched by <c>SourceDomain</c>) are skipped. Safe to run on every deploy
        /// (idempotent). robots.txt compliance is checked lazily by the indexer on first crawl attempt.
        /// </summary>
        public async Task SeedAsync(CancellationToken ct = default)
        {
            var existing = await _db.RecipeIndexSources
                .Select(s => s.SourceDomain)
                .ToHashSetAsync(ct);

            var toAdd = Seeds
                .Where(s => !existing.Contains(s.Domain))
                .ToList();

            if (toAdd.Count == 0)
            {
                _log.LogDebug("[SourceSeed] All {Count} sources already seeded", Seeds.Count);
                return;
            }

            foreach (var seed in toAdd)
            {
                _db.RecipeIndexSources.Add(new RecipeIndexSource
                {
                    SourceName           = seed.Name,
                    SourceDomain         = seed.Domain,
                    ApiEndpoint          = seed.BaseUrl ?? $"https://{seed.Domain}/",
                    BaseUrl              = seed.BaseUrl,
                    LegalNotes           = seed.LegalNotes,
                    IsActive             = true,
                    RespectRobotsTxt     = true,
                    RequestDelayMs       = seed.RequestDelayMs,
                    AllowsThumbnailDisplay = seed.AllowsThumbnail,
                    // robots.txt status: null = not yet checked — indexer checks on first run
                    RobotsAllowed        = null,
                    RobotsCheckedAt      = null,
                });
            }

            await _db.SaveChangesAsync(ct);
            _log.LogInformation("[SourceSeed] Added {Count} new sources", toAdd.Count);
        }

        private record SeedEntry(
            string Name,
            string Domain,
            string? BaseUrl,
            string LegalNotes,
            int RequestDelayMs = 2000,
            bool AllowsThumbnail = false
        );
    }
}
