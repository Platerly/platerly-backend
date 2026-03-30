using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// Public recipe search over the halal-verified RecipeIndex.
    ///
    /// Every recipe returned has already passed the Halal Gate — no non-halal
    /// recipes are ever stored or returned. Source attribution (sourceUrl,
    /// sourceDomain) is always included in every response object.
    ///
    /// GET /api/public/recipes/search
    ///   Params: query, cuisine, maxNickelLoad, excludeAllergens, prepTimeMax, page, pageSize
    ///
    /// GET /api/public/recipes/{id}
    ///   Full recipe detail including ingredients, instructions, allergen analysis,
    ///   and nickel load breakdown.
    ///
    /// Rate limit: 100 requests/min per IP (general policy).
    /// </summary>
    [ApiController]
    [Route("api/public/recipes")]
    [AllowAnonymous]
    [EnableRateLimiting("general")]
    public class PublicRecipesController : ControllerBase
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        // Hard cap — protects against very large in-memory filter sets.
        // TheMealDB contains ~300 recipes; curated seed 50 — well within this.
        private const int MaxCandidates = 2000;

        private readonly PlaterlyDbContext _db;
        private readonly ILogger<PublicRecipesController> _logger;

        public PublicRecipesController(PlaterlyDbContext db, ILogger<PublicRecipesController> logger)
        {
            _db     = db;
            _logger = logger;
        }

        // ── GET /api/public/recipes/search ─────────────────────────────────────

        [HttpGet("search")]
        public async Task<ActionResult<RecipeSearchResponse>> Search(
            [FromQuery] string?  query          = null,
            [FromQuery] string?  cuisine        = null,
            [FromQuery] string?  maxNickelLoad  = null,
            [FromQuery] string?  excludeAllergens = null,
            [FromQuery] int?     prepTimeMax    = null,
            [FromQuery] int      page           = 1,
            [FromQuery] int      pageSize       = 20,
            CancellationToken    ct             = default)
        {
            // ── Clamp params ──────────────────────────────────────────────────
            page     = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 50);

            // ── 1. DB-level filtering ─────────────────────────────────────────

            var q = _db.RecipeIndex
                .AsNoTracking()
                .Include(r => r.Source)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(query))
                q = q.Where(r => EF.Functions.Like(r.Title, $"%{query.Trim()}%"));

            if (!string.IsNullOrWhiteSpace(cuisine))
                q = q.Where(r => r.CuisineType != null &&
                                  EF.Functions.Like(r.CuisineType, $"%{cuisine.Trim()}%"));

            if (prepTimeMax.HasValue)
                q = q.Where(r =>
                    r.PrepTimeMinutes == null ||
                    r.CookTimeMinutes == null ||
                    (r.PrepTimeMinutes + r.CookTimeMinutes) <= prepTimeMax.Value);

            // Load candidates (ordered newest first)
            var candidates = await q
                .OrderByDescending(r => r.IndexedAt)
                .Take(MaxCandidates)
                .Select(r => new
                {
                    r.Id,
                    r.Title,
                    r.CuisineType,
                    ThumbnailUrl = r.ImageDisplayBlocked ? null : r.ThumbnailUrl,
                    r.SourceDomain,
                    r.SourceUrl,
                    r.PrepTimeMinutes,
                    r.CookTimeMinutes,
                    r.TagsJson,
                    r.IngredientsJson,
                    r.IsAnalysed,
                    r.AllergenAnalysisJson,
                    r.NickelLoadJson
                })
                .ToListAsync(ct);

            // ── 2. In-memory filtering ────────────────────────────────────────

            // Nickel level ordering for "max" comparisons
            static int NickelRank(string level) => level switch
            {
                "SAFE"     => 0,
                "MODERATE" => 1,
                "EXCEEDED" => 2,
                _          => -1   // Unknown — not filtered out
            };

            int maxNickelRank = NickelRank(
                (maxNickelLoad ?? "MODERATE").ToUpperInvariant());

            // Allergens to exclude (normalised)
            var excludeSet = string.IsNullOrWhiteSpace(excludeAllergens)
                ? null
                : excludeAllergens
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Trim().ToLowerInvariant())
                    .ToHashSet();

            var results = new List<RecipeSearchResult>();

            foreach (var r in candidates)
            {
                // ── Parse allergen summary ────────────────────────────────────
                var allergenSummary = ParseAllergenSummary(r.AllergenAnalysisJson);

                // ── Parse nickel risk level ───────────────────────────────────
                var nickelRisk = ParseNickelRiskLevel(r.NickelLoadJson);

                // ── Apply nickel filter ───────────────────────────────────────
                if (maxNickelRank >= 0 && r.IsAnalysed)
                {
                    var rank = NickelRank(nickelRisk);
                    if (rank >= 0 && rank > maxNickelRank)
                        continue;
                }

                // ── Apply allergen exclusion filter ───────────────────────────
                if (excludeSet != null && excludeSet.Count > 0 && r.IsAnalysed)
                {
                    var flaggedLower = allergenSummary.FlaggedAllergens
                        .Select(a => a.ToLowerInvariant())
                        .ToHashSet();

                    if (excludeSet.Any(ex => flaggedLower.Any(f => f.Contains(ex) || ex.Contains(f))))
                        continue;
                }

                // ── Build result ──────────────────────────────────────────────
                var tags = ParseTags(r.TagsJson);
                var ingredientCount = ParseIngredientCount(r.IngredientsJson);

                results.Add(new RecipeSearchResult
                {
                    Id              = r.Id,
                    Title           = r.Title,
                    CuisineType     = r.CuisineType,
                    ThumbnailUrl    = r.ThumbnailUrl,
                    SourceDomain    = r.SourceDomain,
                    SourceUrl       = r.SourceUrl,
                    PrepTimeMinutes = r.PrepTimeMinutes,
                    CookTimeMinutes = r.CookTimeMinutes,
                    Tags            = tags,
                    IngredientCount = ingredientCount,
                    IsAnalysed      = r.IsAnalysed,
                    NickelRiskLevel = nickelRisk,
                    AllergenSummary = allergenSummary
                });
            }

            // ── 3. Paginate ───────────────────────────────────────────────────
            var totalCount = results.Count;
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            var paged      = results
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Ok(new RecipeSearchResponse
            {
                TotalCount = totalCount,
                Page       = page,
                PageSize   = pageSize,
                TotalPages = totalPages,
                Results    = paged
            });
        }

        // ── GET /api/public/recipes/{id} ──────────────────────────────────────

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<RecipeDetailResponse>> GetById(
            Guid id, CancellationToken ct = default)
        {
            var recipe = await _db.RecipeIndex
                .AsNoTracking()
                .Include(r => r.Source)
                .FirstOrDefaultAsync(r => r.Id == id, ct);

            if (recipe == null) return NotFound();

            List<string> ingredients;
            List<string> instructions;

            try { ingredients  = JsonSerializer.Deserialize<List<string>>(recipe.IngredientsJson,  JsonOpts) ?? new(); }
            catch { ingredients  = new(); }

            try { instructions = JsonSerializer.Deserialize<List<string>>(recipe.InstructionsJson, JsonOpts) ?? new(); }
            catch { instructions = new(); }

            return Ok(new RecipeDetailResponse
            {
                Id              = recipe.Id,
                Title           = recipe.Title,
                CuisineType     = recipe.CuisineType,
                ThumbnailUrl    = recipe.ImageDisplayBlocked ? null : recipe.ThumbnailUrl,
                SourceDomain    = recipe.SourceDomain,
                SourceUrl       = recipe.SourceUrl,
                OriginalAuthor  = recipe.OriginalAuthor,
                PrepTimeMinutes = recipe.PrepTimeMinutes,
                CookTimeMinutes = recipe.CookTimeMinutes,
                Tags            = ParseTags(recipe.TagsJson),
                Ingredients     = ingredients,
                Instructions    = instructions,
                IsAnalysed      = recipe.IsAnalysed,
                AnalysisVersion = recipe.AnalysisVersion,
                NickelRiskLevel = ParseNickelRiskLevel(recipe.NickelLoadJson),
                AllergenSummary = ParseAllergenSummary(recipe.AllergenAnalysisJson),
                // Full raw analysis JSON included for power consumers
                AllergenAnalysis = recipe.AllergenAnalysisJson != null
                    ? JsonSerializer.Deserialize<JsonElement>(recipe.AllergenAnalysisJson, JsonOpts)
                    : (JsonElement?)null,
                NickelLoad = recipe.NickelLoadJson != null
                    ? JsonSerializer.Deserialize<JsonElement>(recipe.NickelLoadJson, JsonOpts)
                    : (JsonElement?)null,
                HalalVerified = true,
                HalalNote = "Every recipe in the Recipzee index has been individually screened " +
                            "by the Recipzee Halal Gate. Recipes containing pork, alcohol, blood, " +
                            "or any derivative are discarded before storage and never returned."
            });
        }

        // ── JSON parsing helpers ──────────────────────────────────────────────

        private static RecipeAllergenSummary ParseAllergenSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new RecipeAllergenSummary { OverallRiskLevel = "Unknown" };

            try
            {
                var doc = JsonSerializer.Deserialize<RecipeSafetyAuditResponse>(json, JsonOpts);
                if (doc == null) return new RecipeAllergenSummary { OverallRiskLevel = "Unknown" };

                var flagged = doc.AllergenFlags
                    .Select(f => f.Detail)
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .Distinct()
                    .ToList();

                return new RecipeAllergenSummary
                {
                    OverallRiskLevel = doc.OverallRiskLevel,
                    FlaggedAllergens = flagged
                };
            }
            catch { return new RecipeAllergenSummary { OverallRiskLevel = "Unknown" }; }
        }

        private static string ParseNickelRiskLevel(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return "Unknown";
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("riskLevel", out var el) &&
                    el.ValueKind == JsonValueKind.String)
                    return el.GetString() ?? "Unknown";
                return "Unknown";
            }
            catch { return "Unknown"; }
        }

        private static List<string> ParseTags(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new();
            try { return JsonSerializer.Deserialize<List<string>>(json, JsonOpts) ?? new(); }
            catch { return new(); }
        }

        private static int ParseIngredientCount(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return 0;
            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.ValueKind == JsonValueKind.Array
                    ? doc.RootElement.GetArrayLength()
                    : 0;
            }
            catch { return 0; }
        }
    }
}
