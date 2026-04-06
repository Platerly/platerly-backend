using Platerly.Api.DTOs;
using Platerly.Api.Models;
using Platerly.Api.Services.Ingredients;

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

        private readonly IIngredientMatchingService _matcher;
        private readonly ILogger<NickelLoadService> _logger;

        public NickelLoadService(IIngredientMatchingService matcher, ILogger<NickelLoadService> logger)
        {
            _matcher = matcher;
            _logger = logger;
        }

        /// <summary>
        /// Calculates cumulative daily nickel intake from a multi-meal request and returns a
        /// risk assessment against the 150 μg/day clinical threshold (Mislankar &amp; Zirwas 2013).
        ///
        /// Ingredient resolution delegates to <see cref="IIngredientMatchingService"/> which applies
        /// a three-level fallback: exact NormalizedName → alias table lookup (SQL index) →
        /// progressive word-drop (e.g., "extra-lean ground beef" → "ground beef" → "beef").
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

            // ── 2. Resolve ingredient names → GlobalIngredients ───────────────────
            // Three-level fallback: exact NormalizedName → alias table → word-drop.
            // All DB queries are pushed to SQL with indexed lookups (no C# string splitting).

            var resolved = await _matcher.ResolveAsync(allNames, ct);

            // Build a lookup from each input name to its nickel data
            var lookup = resolved.ToDictionary(
                kvp => kvp.Key,
                kvp => ToNickelData(kvp.Value));

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

                        // Structured log for aggregation in Sentry / Application Insights.
                        // Levels attempted: exact NormalizedName → alias table → word-drop.
                        _logger.LogWarning(
                            "NickelMatchFailure: ingredient {IngredientName} from recipe {RecipeId} " +
                            "failed all matching levels (exact, alias, word-drop). " +
                            "MatchLevelsAttempted=[exact,alias,word-drop] MealLabel={MealLabel} Timestamp={Timestamp}",
                            input.Name,
                            meal.RecipeId ?? "unknown",
                            meal.Label,
                            DateTime.UtcNow);
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

        private static NickelIngredientData ToNickelData(GlobalIngredient gi) => new()
        {
            Name = gi.Name,
            NormalizedName = gi.NormalizedName,
            NickelMcgPerServing = gi.NickelMicrogramsPerServing,
            ServingSizeGrams = gi.NickelServingSizeGrams,
            Classification = gi.NickelClassification,
            ConfidenceLevel = gi.NickelConfidenceLevel,
            DataPointCount = gi.NickelDataPointCount,
            StdDev = gi.NickelStandardDeviation,
            HighVariability = gi.NickelHighVariability,
            PreparationNote = gi.NickelPreparationNote
        };

        private class NickelIngredientData
        {
            public string Name { get; set; } = string.Empty;
            public string NormalizedName { get; set; } = string.Empty;
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
