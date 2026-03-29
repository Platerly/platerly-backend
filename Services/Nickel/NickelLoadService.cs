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
