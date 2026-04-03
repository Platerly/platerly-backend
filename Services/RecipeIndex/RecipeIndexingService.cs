using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;
using RecipeIndexRecord = Platerly.Api.Models.RecipeIndex;

namespace Platerly.Api.Services.RecipeIndex
{
    /// <summary>
    /// Fetches recipes from TheMealDB, runs every recipe through the Halal Gate,
    /// and stores only those that pass. Non-halal recipes are discarded and counted.
    ///
    /// Legal basis: ingredient lists and cooking instructions are factual information,
    /// not copyrightable expression. Original source URL is always stored and displayed
    /// prominently as attribution.
    ///
    /// Robots.txt: TheMealDB provides a public API explicitly for third-party use.
    /// The API endpoint is the correct integration method — no scraping is performed.
    ///
    /// Images: thumbnail URLs are stored as links only. Images are never downloaded
    /// or re-hosted. TheMealDB explicitly provides these for display use.
    /// </summary>
    public class RecipeIndexingService : IRecipeIndexingService
    {
        private const string MealDbDomain   = "themealdb.com";
        private const string MealDbBase     = "https://www.themealdb.com/api/json/v1/1";
        private const string MealDbMealPage = "https://www.themealdb.com/meal/";

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly PlaterlyDbContext    _db;
        private readonly IHalalGateService   _halalGate;
        private readonly IHttpClientFactory  _httpFactory;
        private readonly ILogger<RecipeIndexingService> _logger;

        public RecipeIndexingService(
            PlaterlyDbContext db,
            IHalalGateService halalGate,
            IHttpClientFactory httpFactory,
            ILogger<RecipeIndexingService> logger)
        {
            _db          = db;
            _halalGate   = halalGate;
            _httpFactory = httpFactory;
            _logger      = logger;
        }

        // ── Public entry point ────────────────────────────────────────────────

        /// <summary>
        /// Runs a full indexing pass: fetches recipes from TheMealDB (all categories), routes each
        /// through the halal gate, and persists passing recipes. Returns one
        /// <c>IndexingRunStats</c> record per source (currently just TheMealDB + the curated seed).
        /// </summary>
        public async Task<List<IndexingRunStats>> RunAsync(CancellationToken ct = default)
        {
            var results = new List<IndexingRunStats>();

            results.Add(await RunTheMealDbAsync(ct));

            // Open Food Facts: no recipe API available — product database only.
            // Noted for future addition when a suitable recipe data source is found.

            return results;
        }

        // ── TheMealDB ─────────────────────────────────────────────────────────

        private async Task<IndexingRunStats> RunTheMealDbAsync(CancellationToken ct)
        {
            var stats = new IndexingRunStats { Source = "TheMealDB", StartedAt = DateTime.UtcNow };
            var http  = _httpFactory.CreateClient("TheMealDB");

            // Ensure source record exists
            var source = await EnsureSourceAsync(
                name:               "TheMealDB",
                domain:             MealDbDomain,
                endpoint:           MealDbBase,
                legalNotes:         "Ingredient lists and cooking instructions are factual information, " +
                                    "not copyrightable expression. TheMealDB provides a public API " +
                                    "explicitly for third-party integration. Source URL always displayed " +
                                    "as attribution. Thumbnails displayed as remote links — never re-hosted.",
                allowsThumbnails:   true,
                requestDelayMs:     1000,
                ct);

            // Load all existing TheMealDB URLs to enable fast duplicate detection
            var existingUrls = await _db.RecipeIndex
                .Where(r => r.SourceDomain == MealDbDomain)
                .Select(r => r.SourceUrl)
                .ToHashSetAsync(ct);

            _logger.LogInformation("[TheMealDB] Starting. {Existing} recipes already indexed.",
                existingUrls.Count);

            // Fetch categories
            var categories = await FetchCategoriesAsync(http, ct);
            _logger.LogInformation("[TheMealDB] Found {Count} categories.", categories.Count);

            foreach (var category in categories)
            {
                if (ct.IsCancellationRequested) break;

                await Task.Delay(source.RequestDelayMs, ct);

                var meals = await FetchMealsByCategoryAsync(http, category.StrCategory, ct);
                if (meals.Count == 0) continue;

                _logger.LogDebug("[TheMealDB] Category '{Category}' — {Count} meals.",
                    category.StrCategory, meals.Count);

                foreach (var meal in meals)
                {
                    if (ct.IsCancellationRequested) break;

                    var sourceUrl = $"{MealDbMealPage}{meal.IdMeal}";

                    if (existingUrls.Contains(sourceUrl))
                    {
                        stats.TotalSkipped++;
                        continue;
                    }

                    await Task.Delay(source.RequestDelayMs, ct);

                    MealDetail? detail;
                    try
                    {
                        detail = await FetchMealDetailAsync(http, meal.IdMeal, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[TheMealDB] Failed to fetch detail for meal {Id}.", meal.IdMeal);
                        continue;
                    }

                    if (detail == null) continue;

                    var ingredients  = ParseIngredients(detail);
                    var instructions = ParseInstructions(detail.StrInstructions);

                    if (ingredients.Count == 0 || instructions.Count == 0)
                    {
                        _logger.LogDebug("[TheMealDB] Skipping '{Title}' — missing ingredients or instructions.",
                            detail.StrMeal);
                        continue;
                    }

                    stats.TotalFetched++;

                    // ── Halal Gate — the most critical step ────────────────────────
                    var gateResult = _halalGate.Evaluate(ingredients);

                    if (!gateResult.Passed)
                    {
                        stats.TotalDiscarded++;
                        stats.DiscardedTitles.Add(detail.StrMeal);

                        source.TotalRecipesDiscarded++;
                        await _db.SaveChangesAsync(ct);

                        _logger.LogInformation(
                            "[TheMealDB] DISCARDED '{Title}' — non-halal: {Violations}",
                            detail.StrMeal,
                            string.Join(" | ", gateResult.FailedIngredients.Select(
                                v => $"{v.IngredientName} ({v.Category}: {v.MatchedTerm})")));
                        continue;
                    }

                    // ── Store halal-verified recipe ────────────────────────────────
                    var tags = new List<string>();
                    if (!string.IsNullOrWhiteSpace(detail.StrCategory)) tags.Add(detail.StrCategory);
                    if (!string.IsNullOrWhiteSpace(detail.StrArea))     tags.Add(detail.StrArea);

                    var recipe = new RecipeIndexRecord
                    {
                        Title            = detail.StrMeal,
                        SourceUrl        = sourceUrl,
                        SourceDomain     = MealDbDomain,
                        OriginalAuthor   = null,
                        IngredientsJson  = JsonSerializer.Serialize(ingredients),
                        InstructionsJson = JsonSerializer.Serialize(instructions),
                        IndexedAt        = DateTime.UtcNow,
                        ThumbnailUrl     = string.IsNullOrWhiteSpace(detail.StrMealThumb)
                                              ? null : detail.StrMealThumb,
                        CuisineType      = detail.StrArea ?? detail.StrCategory,
                        TagsJson         = tags.Count > 0 ? JsonSerializer.Serialize(tags) : null,
                        IsAnalysed       = false,
                        SourceId         = source.Id
                    };

                    _db.RecipeIndex.Add(recipe);
                    existingUrls.Add(sourceUrl);
                    source.TotalRecipesIndexed++;
                    stats.TotalStored++;

                    // Save in small batches to avoid large uncommitted transactions
                    if (stats.TotalStored % 20 == 0)
                        await _db.SaveChangesAsync(ct);
                }
            }

            source.LastIndexedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            stats.CompletedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "[TheMealDB] Run complete. Fetched={Fetched} Stored={Stored} Discarded={Discarded} Skipped={Skipped} Time={Time}ms",
                stats.TotalFetched, stats.TotalStored, stats.TotalDiscarded, stats.TotalSkipped, stats.ElapsedMs);

            return stats;
        }

        // ── HTTP helpers ──────────────────────────────────────────────────────

        private static async Task<List<MealCategory>> FetchCategoriesAsync(
            HttpClient http, CancellationToken ct)
        {
            try
            {
                var json     = await http.GetStringAsync($"{MealDbBase}/categories.php", ct);
                var response = JsonSerializer.Deserialize<CategoryListResponse>(json, JsonOpts);
                return response?.Categories?.ToList() ?? new List<MealCategory>();
            }
            catch { return new List<MealCategory>(); }
        }

        private static async Task<List<MealSummary>> FetchMealsByCategoryAsync(
            HttpClient http, string category, CancellationToken ct)
        {
            try
            {
                var encoded  = Uri.EscapeDataString(category);
                var json     = await http.GetStringAsync($"{MealDbBase}/filter.php?c={encoded}", ct);
                var response = JsonSerializer.Deserialize<MealFilterResponse>(json, JsonOpts);
                return response?.Meals?.ToList() ?? new List<MealSummary>();
            }
            catch { return new List<MealSummary>(); }
        }

        private static async Task<MealDetail?> FetchMealDetailAsync(
            HttpClient http, string mealId, CancellationToken ct)
        {
            var json = await http.GetStringAsync($"{MealDbBase}/lookup.php?i={mealId}", ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("meals", out var mealsEl) ||
                mealsEl.ValueKind != JsonValueKind.Array ||
                mealsEl.GetArrayLength() == 0)
                return null;

            var m = mealsEl[0];

            var detail = new MealDetail
            {
                IdMeal         = S(m, "idMeal"),
                StrMeal        = S(m, "strMeal") ?? "(Untitled)",
                StrCategory    = S(m, "strCategory"),
                StrArea        = S(m, "strArea"),
                StrInstructions= S(m, "strInstructions"),
                StrMealThumb   = S(m, "strMealThumb"),
            };

            // Extract numbered ingredient/measure pairs
            for (int i = 1; i <= 20; i++)
            {
                detail.Ingredients[i - 1] = S(m, $"strIngredient{i}");
                detail.Measures[i - 1]    = S(m, $"strMeasure{i}");
            }

            return detail;
        }

        // ── Parsing helpers ───────────────────────────────────────────────────

        private static List<string> ParseIngredients(MealDetail meal)
        {
            var result = new List<string>();
            for (int i = 0; i < 20; i++)
            {
                var ing = meal.Ingredients[i]?.Trim();
                if (string.IsNullOrEmpty(ing)) continue;

                var meas    = meal.Measures[i]?.Trim() ?? "";
                var combined = string.IsNullOrWhiteSpace(meas)
                    ? ing
                    : $"{meas} {ing}";
                result.Add(combined);
            }
            return result;
        }

        private static List<string> ParseInstructions(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new List<string>();

            return raw
                .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();
        }

        private static string? S(JsonElement el, string prop)
        {
            if (el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString();
                return string.IsNullOrWhiteSpace(s) ? null : s;
            }
            return null;
        }

        // ── Source management ─────────────────────────────────────────────────

        private async Task<RecipeIndexSource> EnsureSourceAsync(
            string name, string domain, string endpoint, string legalNotes,
            bool allowsThumbnails, int requestDelayMs, CancellationToken ct)
        {
            var source = await _db.RecipeIndexSources
                .FirstOrDefaultAsync(s => s.SourceDomain == domain, ct);

            if (source == null)
            {
                source = new RecipeIndexSource
                {
                    SourceName           = name,
                    SourceDomain         = domain,
                    ApiEndpoint          = endpoint,
                    LegalNotes           = legalNotes,
                    AllowsThumbnailDisplay = allowsThumbnails,
                    RequestDelayMs       = requestDelayMs,
                    IsActive             = true,
                    RequiresApiKey       = false
                };
                _db.RecipeIndexSources.Add(source);
                await _db.SaveChangesAsync(ct);

                _logger.LogInformation("Created RecipeIndexSource '{Name}' (domain: {Domain}).",
                    name, domain);
            }

            return source;
        }

        // ── TheMealDB response models (private) ───────────────────────────────

        private class CategoryListResponse
        {
            [JsonPropertyName("categories")]
            public MealCategory[]? Categories { get; set; }
        }

        private class MealCategory
        {
            [JsonPropertyName("idCategory")]
            public string IdCategory { get; set; } = "";

            [JsonPropertyName("strCategory")]
            public string StrCategory { get; set; } = "";
        }

        private class MealFilterResponse
        {
            [JsonPropertyName("meals")]
            public MealSummary[]? Meals { get; set; }
        }

        private class MealSummary
        {
            [JsonPropertyName("strMeal")]
            public string StrMeal { get; set; } = "";

            [JsonPropertyName("strMealThumb")]
            public string StrMealThumb { get; set; } = "";

            [JsonPropertyName("idMeal")]
            public string IdMeal { get; set; } = "";
        }

        private class MealDetail
        {
            public string IdMeal          { get; set; } = "";
            public string StrMeal         { get; set; } = "";
            public string? StrCategory    { get; set; }
            public string? StrArea        { get; set; }
            public string? StrInstructions{ get; set; }
            public string? StrMealThumb   { get; set; }
            public string?[] Ingredients  { get; } = new string?[20];
            public string?[] Measures     { get; } = new string?[20];
        }
    }
}
