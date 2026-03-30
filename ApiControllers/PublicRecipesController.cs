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

        // Candidate buffer when in-memory filtering is required (nickel/allergen).
        // Enough headroom for filtering to still return a full page, without
        // loading the entire table. Adjusted downward from 2000.
        private const int FilteredCandidateMultiplier = 15;

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

            // ── Nickel level ordering ─────────────────────────────────────────
            static int NickelRank(string level) => level switch
            {
                "SAFE"     => 0,
                "MODERATE" => 1,
                "EXCEEDED" => 2,
                _          => -1   // Unknown — not filtered out
            };

            int maxNickelRank = NickelRank(
                (maxNickelLoad ?? "EXCEEDED").ToUpperInvariant()); // default = show all

            var excludeSet = string.IsNullOrWhiteSpace(excludeAllergens)
                ? null
                : excludeAllergens
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Trim().ToLowerInvariant())
                    .ToHashSet();

            bool needsInMemoryFilter = (maxNickelRank >= 0 && maxNickelRank < 2)
                                       || (excludeSet != null && excludeSet.Count > 0);

            // ── Fast path: no in-memory filtering needed ──────────────────────
            // Paginate directly at the DB level — no full-table candidate scan.
            if (!needsInMemoryFilter)
            {
                var totalCount = await q.CountAsync(ct);
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                var directRows = await q
                    .OrderByDescending(r => r.IndexedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(r => new
                    {
                        r.Id, r.Title, r.CuisineType,
                        ThumbnailUrl = r.ImageDisplayBlocked ? null : r.ThumbnailUrl,
                        r.SourceDomain, r.SourceUrl,
                        r.PrepTimeMinutes, r.CookTimeMinutes,
                        r.TagsJson, r.IngredientsJson, r.IsAnalysed,
                        r.AllergenAnalysisJson, r.NickelLoadJson
                    })
                    .ToListAsync(ct);

                var directResults = directRows.Select(r => new RecipeSearchResult
                {
                    Id              = r.Id,
                    Title           = r.Title,
                    CuisineType     = r.CuisineType,
                    ThumbnailUrl    = r.ThumbnailUrl,
                    SourceDomain    = r.SourceDomain,
                    SourceUrl       = r.SourceUrl,
                    PrepTimeMinutes = r.PrepTimeMinutes,
                    CookTimeMinutes = r.CookTimeMinutes,
                    Tags            = ParseTags(r.TagsJson),
                    IngredientCount = ParseIngredientCount(r.IngredientsJson),
                    IsAnalysed      = r.IsAnalysed,
                    NickelRiskLevel = ParseNickelRiskLevel(r.NickelLoadJson),
                    AllergenSummary = ParseAllergenSummary(r.AllergenAnalysisJson)
                }).ToList();

                return Ok(new RecipeSearchResponse
                {
                    TotalCount = totalCount,
                    Page       = page,
                    PageSize   = pageSize,
                    TotalPages = totalPages,
                    Results    = directResults
                });
            }

            // ── Filtered path: load a bounded candidate set, filter in memory ─
            // Cap at pageSize × multiplier — enough buffer to return a full page
            // after filtering, without loading the entire table.
            var maxCandidates = pageSize * FilteredCandidateMultiplier;

            var candidates = await q
                .OrderByDescending(r => r.IndexedAt)
                .Take(maxCandidates)
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
            var results = new List<RecipeSearchResult>();

            foreach (var r in candidates)
            {
                var allergenSummary = ParseAllergenSummary(r.AllergenAnalysisJson);
                var nickelRisk      = ParseNickelRiskLevel(r.NickelLoadJson);

                if (maxNickelRank >= 0 && r.IsAnalysed)
                {
                    var rank = NickelRank(nickelRisk);
                    if (rank >= 0 && rank > maxNickelRank)
                        continue;
                }

                if (excludeSet != null && excludeSet.Count > 0 && r.IsAnalysed)
                {
                    var flaggedLower = allergenSummary.FlaggedAllergens
                        .Select(a => a.ToLowerInvariant())
                        .ToHashSet();

                    if (excludeSet.Any(ex => flaggedLower.Any(f => f.Contains(ex) || ex.Contains(f))))
                        continue;
                }

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
                    Tags            = ParseTags(r.TagsJson),
                    IngredientCount = ParseIngredientCount(r.IngredientsJson),
                    IsAnalysed      = r.IsAnalysed,
                    NickelRiskLevel = nickelRisk,
                    AllergenSummary = allergenSummary
                });
            }

            // ── 3. Paginate ───────────────────────────────────────────────────
            var filteredTotal  = results.Count;
            var filteredPages  = (int)Math.Ceiling(filteredTotal / (double)pageSize);
            var paged          = results
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Ok(new RecipeSearchResponse
            {
                TotalCount = filteredTotal,
                Page       = page,
                PageSize   = pageSize,
                TotalPages = filteredPages,
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
                // Deserialize into typed DTOs so ASP.NET Core re-serializes with
                // camelCase property names (raw JsonElement would embed PascalCase as-is).
                AllergenAnalysis = recipe.AllergenAnalysisJson != null
                    ? JsonSerializer.Deserialize<RecipeSafetyAuditResponse>(recipe.AllergenAnalysisJson, JsonOpts)
                    : null,
                NickelLoad = recipe.NickelLoadJson != null
                    ? JsonSerializer.Deserialize<NickelDailyLoadResponse>(recipe.NickelLoadJson, JsonOpts)
                    : null,
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
                // Stored JSON uses PascalCase ("RiskLevel") — TryGetProperty is case-sensitive
                if (doc.RootElement.TryGetProperty("RiskLevel", out var el) &&
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
