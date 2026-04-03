using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Nickel
{
    /// <summary>
    /// Calculates cumulative daily nickel intake from a full day's meal plan.
    /// Reference: Mislankar & Zirwas 2013 — 150 μg/day clinical threshold.
    ///
    /// Nickel scaling formula:
    ///   If quantityGrams supplied:  nickelMcg = (quantityGrams / servingSizeGrams) × NickelMicrogramsPerServing
    ///   If quantityGrams omitted:   nickelMcg = NickelMicrogramsPerServing (one reference serving)
    ///   Default servingSizeGrams when null: 100g
    /// </summary>
    public class NickelLoadService : INickelLoadService
    {
        private const int DefaultServingSizeGrams = 100;

        private readonly PlaterlyDbContext _db;
        private readonly ILogger<NickelLoadService> _logger;

        public NickelLoadService(PlaterlyDbContext db, ILogger<NickelLoadService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Calculates cumulative daily nickel intake from a multi-meal request and returns a
        /// risk assessment against the 150 μg/day clinical threshold (Mislankar &amp; Zirwas 2013).
        ///
        /// Ingredient resolution pipeline (three levels):
        /// <list type="number">
        ///   <item>Exact NormalizedName match — bulk query of all unique ingredient names.</item>
        ///   <item>Alias fallback — scans comma-separated <c>GlobalIngredient.Aliases</c> for
        ///     any still-unmatched name.</item>
        ///   <item>Progressive word-drop fallback — drops leading words iteratively to handle
        ///     adjective prefixes from external recipes (e.g., "extra-lean ground beef" →
        ///     "ground beef" → "beef"). Minimum candidate length 3 characters. All candidates are
        ///     bulk-fetched in a single query; shortest suffix match wins.</item>
        /// </list>
        ///
        /// Nickel scaling:
        /// <list type="bullet">
        ///   <item>With quantity: <c>nickelMcg = (quantityGrams / servingSizeGrams) × NickelMicrogramsPerServing</c></item>
        ///   <item>Without quantity: one reference serving (<c>NickelMicrogramsPerServing</c>) assumed.</item>
        ///   <item>Default serving size when null in DB: <see cref="DefaultServingSizeGrams"/> (100g).</item>
        /// </list>
        ///
        /// Risk bands: SAFE ≤ <c>NickelThresholds.SafePercent</c> of threshold;
        /// MODERATE ≤ <c>NickelThresholds.ExceededPercent</c>; EXCEEDED above that.
        /// </summary>
        /// <param name="request">Meals with ingredient names, optional gram quantities, and meal labels.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Daily nickel load response with per-meal breakdown, top contributors, risk level, and unmatched names.</returns>
        public async Task<NickelDailyLoadResponse> CalculateDailyLoadAsync(
            NickelDailyLoadRequest request,
            CancellationToken ct = default)
        {
            // ── 1. Collect all unique ingredient names across all meals ───────────

            var allNames = request.Meals
                .SelectMany(m => m.Ingredients.Select(i => i.Name.Trim().ToLower()))
                .Distinct()
                .ToList();

            // ── 2. Bulk fetch matching GlobalIngredients ──────────────────────────

            // Match on NormalizedName first; also check aliases as a fallback.
            var ingredientData = await _db.GlobalIngredients
                .AsNoTracking()
                .Where(gi => gi.IsActive && allNames.Contains(gi.NormalizedName))
                .Select(gi => new NickelIngredientData
                {
                    Name = gi.Name,
                    NormalizedName = gi.NormalizedName,
                    Aliases = gi.Aliases,
                    NickelMcgPerServing = gi.NickelMicrogramsPerServing,
                    ServingSizeGrams = gi.NickelServingSizeGrams,
                    Classification = gi.NickelClassification,
                    ConfidenceLevel = gi.NickelConfidenceLevel,
                    DataPointCount = gi.NickelDataPointCount,
                    StdDev = gi.NickelStandardDeviation,
                    HighVariability = gi.NickelHighVariability,
                    PreparationNote = gi.NickelPreparationNote
                })
                .ToListAsync(ct);

            // Build lookup: normalized name → data
            var lookup = ingredientData.ToDictionary(d => d.NormalizedName);

            // Alias fallback: for names not directly matched, check aliases
            var unmatched = allNames.Except(lookup.Keys).ToList();
            if (unmatched.Any())
            {
                var aliasCandidates = await _db.GlobalIngredients
                    .AsNoTracking()
                    .Where(gi => gi.IsActive && gi.Aliases != null)
                    .Select(gi => new NickelIngredientData
                    {
                        Name = gi.Name,
                        NormalizedName = gi.NormalizedName,
                        Aliases = gi.Aliases,
                        NickelMcgPerServing = gi.NickelMicrogramsPerServing,
                        ServingSizeGrams = gi.NickelServingSizeGrams,
                        Classification = gi.NickelClassification,
                        ConfidenceLevel = gi.NickelConfidenceLevel,
                        DataPointCount = gi.NickelDataPointCount,
                        StdDev = gi.NickelStandardDeviation,
                        HighVariability = gi.NickelHighVariability,
                        PreparationNote = gi.NickelPreparationNote
                    })
                    .ToListAsync(ct);

                foreach (var candidate in aliasCandidates)
                {
                    if (candidate.Aliases == null) continue;
                    var aliases = candidate.Aliases
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(a => a.ToLower());
                    foreach (var alias in aliases)
                    {
                        if (unmatched.Contains(alias) && !lookup.ContainsKey(alias))
                            lookup[alias] = candidate;
                    }
                }
            }

            // ── Word-drop fallback ────────────────────────────────────────────────
            // For names still unmatched after exact + alias lookup, progressively
            // drop leading words to handle adjective prefixes from web recipes:
            // "extra-lean ground beef" → "ground beef" → "beef"
            // "fresh blueberries"      → "blueberries"
            var stillUnmatched = allNames.Except(lookup.Keys).ToList();
            if (stillUnmatched.Any())
            {
                // Collect all word-drop candidates so we can bulk-fetch in one query
                var wordDropMap = new Dictionary<string, string>(); // candidate → original name
                foreach (var name in stillUnmatched)
                {
                    var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 1; i < words.Length; i++)
                    {
                        var candidate = string.Join(" ", words.Skip(i));
                        if (candidate.Length >= 3 && !wordDropMap.ContainsKey(candidate))
                            wordDropMap[candidate] = name;
                    }
                }

                if (wordDropMap.Any())
                {
                    var candidateKeys = wordDropMap.Keys.ToList();
                    var wordDropMatches = await _db.GlobalIngredients
                        .AsNoTracking()
                        .Where(gi => gi.IsActive && candidateKeys.Contains(gi.NormalizedName))
                        .Select(gi => new NickelIngredientData
                        {
                            Name = gi.Name,
                            NormalizedName = gi.NormalizedName,
                            Aliases = gi.Aliases,
                            NickelMcgPerServing = gi.NickelMicrogramsPerServing,
                            ServingSizeGrams = gi.NickelServingSizeGrams,
                            Classification = gi.NickelClassification,
                            ConfidenceLevel = gi.NickelConfidenceLevel,
                            DataPointCount = gi.NickelDataPointCount,
                            StdDev = gi.NickelStandardDeviation,
                            HighVariability = gi.NickelHighVariability,
                            PreparationNote = gi.NickelPreparationNote
                        })
                        .ToListAsync(ct);

                    // Map each matched candidate back to the original unmatched name
                    // Prefer shorter suffix (more specific match wins — stop at first hit)
                    foreach (var match in wordDropMatches)
                    {
                        var originalName = wordDropMap[match.NormalizedName];
                        if (!lookup.ContainsKey(originalName))
                            lookup[originalName] = match;
                    }
                }
            }

            // ── 3. Calculate per-meal, per-ingredient nickel ──────────────────────

            var mealResults = new List<NickelMealResult>();
            var allIngredientResults = new List<NickelIngredientResult>();
            var unmatchedNames = new List<string>();

            foreach (var meal in request.Meals)
            {
                var mealIngredients = new List<NickelIngredientResult>();

                foreach (var input in meal.Ingredients)
                {
                    var normalized = input.Name.Trim().ToLower();
                    var result = new NickelIngredientResult
                    {
                        Name = input.Name,
                        MealLabel = meal.Label,
                        QuantityGrams = input.QuantityGrams
                    };

                    if (lookup.TryGetValue(normalized, out var data))
                    {
                        result.Matched = true;
                        result.ReferenceServingSizeGrams = data.ServingSizeGrams ?? DefaultServingSizeGrams;
                        result.NickelPerServingMcg = data.NickelMcgPerServing;
                        result.NickelClassification = data.Classification.ToString();
                        result.DataConfidence = data.ConfidenceLevel.ToString();
                        result.HighVariability = data.HighVariability;
                        result.PreparationNote = data.PreparationNote;

                        if (data.NickelMcgPerServing.HasValue)
                        {
                            int servingGrams = data.ServingSizeGrams ?? DefaultServingSizeGrams;
                            decimal nickelPerServing = data.NickelMcgPerServing.Value;

                            if (input.QuantityGrams.HasValue && input.QuantityGrams.Value > 0)
                            {
                                result.NickelContributionMcg = Math.Round(
                                    (input.QuantityGrams.Value / servingGrams) * nickelPerServing, 3);
                            }
                            else
                            {
                                // No quantity — assume one reference serving
                                result.NickelContributionMcg = Math.Round(nickelPerServing, 3);
                            }
                        }
                        else
                        {
                            // Matched but no nickel measurement
                            result.NickelContributionMcg = 0;
                            result.DataConfidence = "Unknown";
                        }
                    }
                    else
                    {
                        result.Matched = false;
                        result.NickelContributionMcg = 0;
                        result.NickelClassification = "Unknown";
                        result.DataConfidence = "Unknown";

                        if (!unmatchedNames.Contains(input.Name, StringComparer.OrdinalIgnoreCase))
                            unmatchedNames.Add(input.Name);
                    }

                    mealIngredients.Add(result);
                    allIngredientResults.Add(result);
                }

                decimal mealTotal = mealIngredients.Sum(i => i.NickelContributionMcg);

                // Set contribution percent per ingredient (relative to daily threshold)
                foreach (var ing in mealIngredients)
                {
                    ing.ContributionPercent = NickelThresholds.DailyThresholdMcg > 0
                        ? Math.Round(ing.NickelContributionMcg / NickelThresholds.DailyThresholdMcg * 100, 2)
                        : 0;
                }

                mealResults.Add(new NickelMealResult
                {
                    Label = meal.Label,
                    NickelMcg = Math.Round(mealTotal, 3),
                    ThresholdPercent = Math.Round(mealTotal / NickelThresholds.DailyThresholdMcg * 100, 2),
                    Ingredients = mealIngredients
                        .OrderByDescending(i => i.NickelContributionMcg)
                        .ToList()
                });
            }

            // ── 4. Calculate totals ───────────────────────────────────────────────

            decimal totalNickel = Math.Round(allIngredientResults.Sum(i => i.NickelContributionMcg), 3);
            decimal thresholdPct = Math.Round(totalNickel / NickelThresholds.DailyThresholdMcg * 100, 2);
            decimal remaining = Math.Round(NickelThresholds.DailyThresholdMcg - totalNickel, 3);

            string riskLevel = thresholdPct <= NickelThresholds.SafePercent ? "SAFE"
                : thresholdPct <= NickelThresholds.ExceededPercent ? "MODERATE"
                : "EXCEEDED";

            // Top contributors sorted by nickel contribution
            var topContributors = allIngredientResults
                .Where(i => i.Matched)
                .OrderByDescending(i => i.NickelContributionMcg)
                .ToList();

            int matchedCount = allIngredientResults.Count(i => i.Matched);

            return new NickelDailyLoadResponse
            {
                TotalNickelMcg = totalNickel,
                ThresholdPercent = thresholdPct,
                RiskLevel = riskLevel,
                RemainingBudgetMcg = remaining,
                DailyThresholdMcg = NickelThresholds.DailyThresholdMcg,
                ThresholdCitation = NickelThresholds.Citation,
                Meals = mealResults,
                TopContributors = topContributors,
                MatchedIngredients = matchedCount,
                TotalIngredients = allIngredientResults.Count,
                UnmatchedIngredients = unmatchedNames
            };
        }

        private class NickelIngredientData
        {
            public string Name { get; set; } = string.Empty;
            public string NormalizedName { get; set; } = string.Empty;
            public string? Aliases { get; set; }
            public decimal? NickelMcgPerServing { get; set; }
            public int? ServingSizeGrams { get; set; }
            public NickelClassification Classification { get; set; }
            public NickelConfidenceLevel ConfidenceLevel { get; set; }
            public int? DataPointCount { get; set; }
            public decimal? StdDev { get; set; }
            public bool HighVariability { get; set; }
            public string? PreparationNote { get; set; }
        }
    }
}
