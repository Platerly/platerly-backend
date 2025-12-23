using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Allergy
{
    internal static class SeverityHelpers
    {
        public static int ToRank(string? s) => s?.ToLowerInvariant() switch
        {
            "high" => 3,
            "medium" => 2,
            "low" => 1,
            _ => 1
        };

        public static string FromRank(int r) => r switch
        {
            >= 3 => "High",
            2 => "Medium",
            _ => "Low"
        };
    }

    public sealed class AllergyAlertService : IAllergyAlertService
    {
        private readonly PlaterlyDbContext _db;
        public AllergyAlertService(PlaterlyDbContext db) => _db = db;

        public async Task<RecipeAlertsResponse> GetAlertsForRecipeAsync(Guid recipeId, Guid userId, CancellationToken ct = default)
        {
            // 1) User prefs (enabled only) — project enum -> string
            var userPrefs = await _db.UserAllergies
                .AsNoTracking()
                .Where(x => x.UserId == userId && x.IsEnabled)
                .Select(x => new
                {
                    x.AllergyId,
                    AllergyName = x.Allergy.Name,
                    x.IncludeTrace,
                    MinSeverity = x.MinSeverity.ToString()
                })
                .ToListAsync(ct);

            if (userPrefs.Count == 0)
                return new RecipeAlertsResponse { RecipeId = recipeId };

            // Normalize user enabled set into keys
            var userPrefByKey = userPrefs
                .GroupBy(p => AllergenNameNormalizer.Key(p.AllergyName))
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            var enabledKeys = new HashSet<string>(userPrefByKey.Keys, StringComparer.Ordinal);

            // 2) Recipe ingredients (Guid ids)
            var recIngs = await _db.Ingredients
                .AsNoTracking()
                .Where(i => i.RecipeId == recipeId)
                .Select(i => new
                {
                    i.Id,                 // Guid
                    i.Name,
                    i.GlobalIngredientId  // Guid
                })
                .ToListAsync(ct);

            if (recIngs.Count == 0)
                return new RecipeAlertsResponse { RecipeId = recipeId };

            var ingredientIds = recIngs.Select(x => x.Id).ToList();

            // 3) Per-recipe overrides (enum -> string) + normalized allergen key
            var overrides = await _db.IngredientAllergens
                .AsNoTracking()
                .Where(ia => ingredientIds.Contains(ia.IngredientId))
                .Select(ia => new
                {
                    ia.IngredientId,
                    AllergenKey = AllergenNameNormalizer.Key(ia.AllergenType),
                    Severity = ia.Severity.ToString(),
                    ia.MayContain,
                    ia.CrossContactRisk,
                    ia.ProcessingAid
                })
                .ToListAsync(ct);

            // 4) Global mappings — ignore Guid.Empty
            var globalIds = recIngs
                .Select(x => x.GlobalIngredientId)
                .Where(g => g != Guid.Empty)
                .Distinct()
                .ToList();

            var globals = await _db.GlobalIngredientAllergens
                .AsNoTracking()
                .Where(ga => globalIds.Contains(ga.GlobalIngredientId))
                .Select(ga => new
                {
                    ga.GlobalIngredientId,
                    AllergenKey = AllergenNameNormalizer.Key(ga.AllergenType),
                    Severity = ga.Severity.ToString(),
                    ga.MayContain,
                    ga.CrossContactRisk,
                    ga.ProcessingAid
                })
                .ToListAsync(ct);

            // Indexes (keys already normalized)
            var overIdx = overrides
                .GroupBy(o => (o.IngredientId, o.AllergenKey))
                .ToDictionary(g => (g.Key.IngredientId, g.Key.AllergenKey), g => g.First());

            var globIdx = globals
                .GroupBy(g => (g.GlobalIngredientId, g.AllergenKey))
                .ToDictionary(g => (g.Key.GlobalIngredientId, g.Key.AllergenKey), g => g.First());

            var grouped = new Dictionary<string, AllergyAlertDto>(StringComparer.OrdinalIgnoreCase);

            foreach (var ing in recIngs)
            {
                var candidateKeys = new HashSet<string>(StringComparer.Ordinal);

                foreach (var ov in overrides.Where(x => x.IngredientId == ing.Id))
                    candidateKeys.Add(ov.AllergenKey);

                if (ing.GlobalIngredientId != Guid.Empty)
                {
                    var gid = ing.GlobalIngredientId;
                    foreach (var gl in globals.Where(x => x.GlobalIngredientId == gid))
                        candidateKeys.Add(gl.AllergenKey);
                }

                if (candidateKeys.Count == 0) continue;

                foreach (var allergenKey in candidateKeys)
                {
                    if (!enabledKeys.Contains(allergenKey))
                        continue;

                    overIdx.TryGetValue((ing.Id, allergenKey), out var ovRow);

                    var hasGlobal = false;
                    dynamic? glRow = null;
                    if (ing.GlobalIngredientId != Guid.Empty)
                    {
                        hasGlobal = globIdx.TryGetValue((ing.GlobalIngredientId, allergenKey), out var gRow);
                        glRow = gRow;
                    }

                    if (ovRow is null && !hasGlobal) continue;

                    string resolvedSeverity = ovRow?.Severity ?? glRow?.Severity ?? "Low";
                    bool resolvedMayContain = ovRow?.MayContain ?? glRow?.MayContain ?? false;
                    bool resolvedCross = ovRow?.CrossContactRisk ?? glRow?.CrossContactRisk ?? false;
                    bool resolvedAid = ovRow?.ProcessingAid ?? glRow?.ProcessingAid ?? false;

                    bool isTraceOnly = resolvedMayContain || resolvedCross || resolvedAid;

                    var pref = userPrefByKey[allergenKey];
                    if (!pref.IncludeTrace && isTraceOnly) continue;
                    if (SeverityHelpers.ToRank(resolvedSeverity) < SeverityHelpers.ToRank(pref.MinSeverity)) continue;

                    // Nice display label for this bucket from the external normalizer
                    var displayLabel = AllergenNameNormalizer.Display(allergenKey);

                    if (!grouped.TryGetValue(displayLabel, out var bucket))
                    {
                        bucket = new AllergyAlertDto
                        {
                            AllergenType = displayLabel,
                            HighestSeverity = "Low",
                            Ingredients = new()
                        };
                        grouped[displayLabel] = bucket;
                    }

                    bucket.Ingredients.Add(new IngredientAlertDetailDto
                    {
                        IngredientId = ing.Id,
                        IngredientName = ing.Name,
                        Source = ovRow is not null ? "Override" : "Global",
                        SeverityResolved = resolvedSeverity,
                        MayContain = resolvedMayContain,
                        CrossContactRisk = resolvedCross,
                        ProcessingAid = resolvedAid,
                        IsTraceOnly = isTraceOnly
                    });

                    var agg = SeverityHelpers.ToRank(bucket.HighestSeverity);
                    var cur = SeverityHelpers.ToRank(resolvedSeverity);
                    if (cur > agg) bucket.HighestSeverity = SeverityHelpers.FromRank(cur);
                }
            }

            return new RecipeAlertsResponse
            {
                RecipeId = recipeId,
                Alerts = grouped.Values
                    .OrderByDescending(a => SeverityHelpers.ToRank(a.HighestSeverity))
                    .ThenBy(a => a.AllergenType)
                    .ToList()
            };
        }
    }
}
