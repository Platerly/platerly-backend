using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Services.Analytics;
using Platerly.Api.Services.Nickel;

namespace Platerly.Api.Services.RecipeIndex
{
    /// <summary>
    /// Processes RecipeIndex rows where IsAnalysed=false.
    ///
    /// For each recipe it runs:
    ///   1. Allergen intelligence  — IRecipeSafetyService  → AllergenAnalysisJson
    ///   2. Nickel load            — INickelLoadService     → NickelLoadJson
    ///
    /// Each analysis step is wrapped in its own try-catch so a single failure
    /// does not block the rest of the recipe or the rest of the batch.
    ///
    /// After analysis the row is marked IsAnalysed=true, AnalysisVersion="1.0".
    /// Recipes are processed in batches of 10 with a 500 ms inter-batch delay.
    /// </summary>
    public class RecipeAnalysisPipelineService : IRecipeAnalysisPipelineService
    {
        private const int    BatchSize        = 10;
        private const int    BatchDelayMs     = 500;
        private const int    LogEvery         = 100;
        private const string AnalysisVersion  = "1.0";

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        // Common units stripped from the front of TheMealDB ingredient strings.
        // Format: "{measure} {name}" e.g. "2 cups Basmati Rice" → "Basmati Rice"
        private static readonly HashSet<string> UnitWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "cups","cup","tbsp","tsp","tablespoon","tablespoons","teaspoon","teaspoons",
            "oz","ounce","ounces","lb","lbs","pound","pounds",
            "kg","g","grams","gram","ml","l","litre","litres","liter","liters",
            "pinch","handful","cloves","clove","bunch","sprigs","sprig",
            "slices","slice","pieces","piece","can","cans","stick","sticks",
            "of","to","taste","large","medium","small","whole"
        };

        private readonly PlaterlyDbContext     _db;
        private readonly IRecipeSafetyService  _safety;
        private readonly INickelLoadService    _nickel;
        private readonly ILogger<RecipeAnalysisPipelineService> _logger;

        public RecipeAnalysisPipelineService(
            PlaterlyDbContext db,
            IRecipeSafetyService safety,
            INickelLoadService nickel,
            ILogger<RecipeAnalysisPipelineService> logger)
        {
            _db     = db;
            _safety = safety;
            _nickel = nickel;
            _logger = logger;
        }

        // ── Public entry point ─────────────────────────────────────────────────

        public async Task<AnalysisPipelineStats> RunAsync(CancellationToken ct = default)
        {
            var stats = new AnalysisPipelineStats { StartedAt = DateTime.UtcNow };

            var totalPending = await _db.RecipeIndex.CountAsync(r => !r.IsAnalysed, ct);

            if (totalPending == 0)
            {
                _logger.LogInformation("[RecipeAnalysis] No unanalysed recipes — pipeline idle.");
                stats.CompletedAt = DateTime.UtcNow;
                return stats;
            }

            _logger.LogInformation("[RecipeAnalysis] Starting. {Pending} recipes to analyse.", totalPending);

            while (!ct.IsCancellationRequested)
            {
                var batch = await _db.RecipeIndex
                    .Where(r => !r.IsAnalysed)
                    .OrderBy(r => r.IndexedAt)
                    .Take(BatchSize)
                    .ToListAsync(ct);

                if (batch.Count == 0) break;

                foreach (var recipe in batch)
                {
                    if (ct.IsCancellationRequested) break;

                    try
                    {
                        await AnalyseRecipeAsync(recipe, ct);
                        stats.TotalProcessed++;
                    }
                    catch (Exception ex)
                    {
                        // Mark as analysed even on total failure so we don't loop on bad data
                        recipe.IsAnalysed      = true;
                        recipe.AnalysisVersion = AnalysisVersion + "-error";
                        stats.TotalFailed++;
                        _logger.LogWarning(ex, "[RecipeAnalysis] Failed on '{Title}' ({Id}).",
                            recipe.Title, recipe.Id);
                    }
                }

                await _db.SaveChangesAsync(ct);

                if (stats.TotalProcessed % LogEvery == 0 && stats.TotalProcessed > 0)
                    _logger.LogInformation("[RecipeAnalysis] Progress: {Done} processed, {Failed} failed.",
                        stats.TotalProcessed, stats.TotalFailed);

                if (!ct.IsCancellationRequested)
                    await Task.Delay(BatchDelayMs, ct);
            }

            stats.CompletedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "[RecipeAnalysis] Run complete. Processed={Processed} Failed={Failed} Time={Time}ms",
                stats.TotalProcessed, stats.TotalFailed, stats.ElapsedMs);

            return stats;
        }

        // ── Per-recipe analysis ────────────────────────────────────────────────

        private async Task AnalyseRecipeAsync(Models.RecipeIndex recipe, CancellationToken ct)
        {
            List<string> rawIngredients;
            try
            {
                rawIngredients = JsonSerializer.Deserialize<List<string>>(recipe.IngredientsJson, JsonOpts)
                                 ?? new List<string>();
            }
            catch
            {
                rawIngredients = new List<string>();
            }

            if (rawIngredients.Count == 0)
            {
                recipe.IsAnalysed      = true;
                recipe.AnalysisVersion = AnalysisVersion;
                return;
            }

            // Strip measure tokens once — used by both allergen and nickel analysis.
            // Web-indexed ingredients arrive as "2 cups Basmati Rice"; stripping yields
            // "Basmati Rice" so GlobalIngredient exact-name lookups can match.
            var strippedIngredients = rawIngredients
                .Select(StripMeasure)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            // ── 1. Allergen intelligence ──────────────────────────────────────

            try
            {
                var safetyResult = await _safety.AuditRecipeAsync(
                    new RecipeSafetyAuditRequest { Ingredients = strippedIngredients }, ct);

                recipe.AllergenAnalysisJson = JsonSerializer.Serialize(safetyResult);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RecipeAnalysis] Allergen audit failed for '{Title}'.", recipe.Title);
            }

            // ── 2. Nickel load ────────────────────────────────────────────────

            try
            {
                var nickelInputs = strippedIngredients
                    .Select(ing => new NickelIngredientInput { Name = ing })
                    .ToList();

                if (nickelInputs.Count > 0)
                {
                    var nickelResult = await _nickel.CalculateDailyLoadAsync(
                        new NickelDailyLoadRequest
                        {
                            Meals = new List<NickelMealInput>
                            {
                                new() { Label = "Recipe", Ingredients = nickelInputs }
                            }
                        }, ct);

                    recipe.NickelLoadJson = JsonSerializer.Serialize(nickelResult);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RecipeAnalysis] Nickel analysis failed for '{Title}'.", recipe.Title);
            }

            // ── Mark as analysed ──────────────────────────────────────────────

            recipe.IsAnalysed      = true;
            recipe.AnalysisVersion = AnalysisVersion;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Strips leading measure tokens from a TheMealDB ingredient string.
        /// "2 cups Basmati Rice" → "Basmati Rice"
        /// "1 tsp cumin"         → "cumin"
        /// "Salt"                → "Salt"
        /// </summary>
        private static string StripMeasure(string ingredient)
        {
            var words = ingredient.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int skip = 0;

            foreach (var word in words)
            {
                if (IsMeasureToken(word))
                    skip++;
                else
                    break;
            }

            return string.Join(" ", words.Skip(skip)).Trim();
        }

        private static readonly Regex NumberPattern =
            new(@"^\d+([./]\d+)?$", RegexOptions.Compiled);

        private static bool IsMeasureToken(string word) =>
            NumberPattern.IsMatch(word) || UnitWords.Contains(word);
    }
}
