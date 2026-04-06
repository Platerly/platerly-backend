using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Helpers;
using Platerly.Api.Models;
using Platerly.Api.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Platerly.Api.Services.Allergy
{
    /// <summary>
    /// B2C user logic layer — generates per-recipe allergen alerts scoped to the authenticated user's
    /// own allergen profile. Depends on <see cref="PlaterlyDbContext"/> and <see cref="IAllergenResolver"/>.
    ///
    /// Alert resolution follows a three-source priority chain for each of the user's registered allergens:
    ///   1. <c>GlobalIngredientAllergens</c> — curated database rows linked to <c>GlobalIngredients</c>.
    ///   2. <c>IngredientAllergens</c> overrides — recipe-specific or user-contributed corrections (Source = "Override").
    ///   3. <see cref="AllergenResolver"/> keyword-expansion fallback — used only when steps 1 and 2
    ///      produce zero hits for a given allergen root, avoiding false silences on unstructured data.
    ///
    /// The response groups hits by allergen type, computes the highest severity across all matching
    /// ingredients, and returns a list of <c>AllergyAlertDto</c> ordered alphabetically by allergen name.
    /// </summary>
    public class AllergyAlertService : IAllergyAlertService
    {
        private readonly PlaterlyDbContext _db;
        private readonly IAllergenResolver _resolver;

        public AllergyAlertService(PlaterlyDbContext db, IAllergenResolver resolver)
        {
            _db = db;
            _resolver = resolver;
        }

        /// <inheritdoc/>
        public List<string> ExpandViewerKeywords(IReadOnlyList<string> userKeywords, string minSeverityOrEmpty)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(minSeverityOrEmpty))
                {
                    var withSeverity = _resolver.ExpandWithSeverity(userKeywords);
                    var filtered = _resolver.FilterByMinSeverity(withSeverity, minSeverityOrEmpty);
                    if (filtered != null && filtered.Count > 0)
                        return filtered.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                }

                var expanded = _resolver.ExpandKeywords(userKeywords);
                if (expanded != null && expanded.Count > 0)
                    return expanded.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch
            {
                // Fall through to raw keywords if resolver throws
            }

            return (userKeywords ?? Array.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static AllergenResolver.Severity ParseSeverity(string? s)
            => (s ?? "").Trim().ToLowerInvariant() switch
            {
                "high" => AllergenResolver.Severity.High,
                "medium" => AllergenResolver.Severity.Medium,
                "moderate" => AllergenResolver.Severity.Medium, // handle legacy text
                _ => AllergenResolver.Severity.Low
            };

        private static string SevToString(AllergenResolver.Severity s)
            => s == AllergenResolver.Severity.High ? "high"
             : s == AllergenResolver.Severity.Medium ? "medium"
             : "low";

        private static AllergenResolver.Severity Map(AllergenSeverity s)
            => s switch
            {
                AllergenSeverity.High => AllergenResolver.Severity.High,
                AllergenSeverity.Medium => AllergenResolver.Severity.Medium,
                _ => AllergenResolver.Severity.Low
            };

        /// <summary>
        /// Produces allergen alerts for a single recipe, filtered to the authenticated user's profile.
        /// Returns an empty alert list (not an error) when the user has no registered allergens or
        /// when the recipe has no ingredients.
        /// </summary>
        /// <param name="recipeId">The recipe to audit.</param>
        /// <param name="userId">The user whose allergen profile is applied as the filter.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A <see cref="RecipeAlertsResponse"/> containing grouped, severity-ranked alerts.</returns>
        public async Task<RecipeAlertsResponse> GetAlertsForRecipeAsync(Guid recipeId, Guid userId, CancellationToken ct = default)
        {
            // ---------------- user allergen types + preferences ----------------
            var userAllergenRows = await (
                from ua in _db.UserAllergies.AsNoTracking()
                join a in _db.Allergies.AsNoTracking() on ua.AllergyId equals a.Id
                where ua.UserId == userId && ua.IsEnabled
                select new { AllergenName = a.Name, ua.MinSeverity, ua.IncludeTrace }
            ).ToListAsync(ct);

            var roots = userAllergenRows
                .Where(x => !string.IsNullOrWhiteSpace(x.AllergenName))
                .Select(x => x.AllergenName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (roots.Count == 0)
            {
                return new RecipeAlertsResponse { RecipeId = recipeId, Alerts = new List<AllergyAlertDto>() };
            }

            // Per-allergen preferences: take the most permissive row if duplicates exist
            // (lowest MinSeverity, true IncludeTrace wins).
            var allergenPrefs = userAllergenRows
                .Where(x => !string.IsNullOrWhiteSpace(x.AllergenName))
                .GroupBy(x => x.AllergenName.Trim().ToLowerInvariant())
                .ToDictionary(
                    g => g.Key,
                    g => (MinSeverity: g.Min(x => x.MinSeverity), IncludeTrace: g.Any(x => x.IncludeTrace))
                );

            var rootLower = roots.Select(x => x.ToLowerInvariant()).Distinct().ToList();
            var displayByLower = roots
                .GroupBy(x => x.Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First().Trim());

            // ---------------- recipe ingredients ----------------
            // Ingredient.Name is [NotMapped] and depends on GlobalIngredient navigation (often null in AsNoTracking).
            // So we JOIN GlobalIngredients explicitly.
            var recipeIngredients = await (
                from i in _db.Ingredients.AsNoTracking()
                where i.RecipeId == recipeId
                join g in _db.GlobalIngredients.AsNoTracking() on i.GlobalIngredientId equals g.Id into gj
                from g in gj.DefaultIfEmpty()
                select new
                {
                    IngredientId = i.Id,
                    GlobalIngredientId = i.GlobalIngredientId,
                    GlobalName = g != null ? g.Name : null
                }
            ).ToListAsync(ct);

            if (recipeIngredients.Count == 0)
            {
                return new RecipeAlertsResponse { RecipeId = recipeId, Alerts = new List<AllergyAlertDto>() };
            }

            var hitMap = new Dictionary<(string allergyLower, Guid ingredientId), IngredientAlertDetailDto>();

            // ---------------- 1) GlobalIngredientAllergens ----------------
            var globalIds = recipeIngredients.Select(x => x.GlobalIngredientId).Distinct().ToList();

            var globalHits = await (
                from ga in _db.GlobalIngredientAllergens.AsNoTracking()
                where globalIds.Contains(ga.GlobalIngredientId)
                select new
                {
                    ga.GlobalIngredientId,
                    ga.AllergenType,
                    ga.Severity,
                    ga.MayContain,
                    ga.CrossContactRisk,
                    ga.ProcessingAid
                }
            ).ToListAsync(ct);

            foreach (var gh in globalHits)
            {
                var aType = (gh.AllergenType ?? "").Trim();
                if (aType.Length == 0) continue;

                var aLower = aType.ToLowerInvariant();
                if (!rootLower.Contains(aLower)) continue;

                foreach (var ing in recipeIngredients.Where(x => x.GlobalIngredientId == gh.GlobalIngredientId))
                {
                    var resolvedName = (ing.GlobalName ?? "").Trim();
                    if (resolvedName.Length == 0) continue;

                    var key = (aLower, ing.IngredientId);
                    var sev = Map(gh.Severity);

                    if (!hitMap.TryGetValue(key, out var dto))
                    {
                        hitMap[key] = new IngredientAlertDetailDto
                        {
                            IngredientId = ing.IngredientId,
                            IngredientName = resolvedName,
                            Source = "Global",
                            SeverityResolved = SevToString(sev),
                            MayContain = gh.MayContain,
                            CrossContactRisk = gh.CrossContactRisk,
                            ProcessingAid = gh.ProcessingAid,
                            IsTraceOnly = false
                        };
                    }
                    else
                    {
                        var cur = ParseSeverity(dto.SeverityResolved);
                        if (sev > cur) dto.SeverityResolved = SevToString(sev);

                        dto.MayContain |= gh.MayContain;
                        dto.CrossContactRisk |= gh.CrossContactRisk;
                        dto.ProcessingAid |= gh.ProcessingAid;
                    }
                }
            }

            // ---------------- 2) IngredientAllergens (overrides) ----------------
            var ingredientIds = recipeIngredients.Select(x => x.IngredientId).Distinct().ToList();

            var overrideHits = await (
                from ia in _db.IngredientAllergens.AsNoTracking()
                where ingredientIds.Contains(ia.IngredientId)
                select new
                {
                    ia.IngredientId,
                    ia.AllergenType,
                    ia.Severity,
                    ia.MayContain,
                    ia.CrossContactRisk,
                    ia.ProcessingAid
                }
            ).ToListAsync(ct);

            foreach (var oh in overrideHits)
            {
                var aType = (oh.AllergenType ?? "").Trim();
                if (aType.Length == 0) continue;

                var aLower = aType.ToLowerInvariant();
                if (!rootLower.Contains(aLower)) continue;

                var ing = recipeIngredients.FirstOrDefault(x => x.IngredientId == oh.IngredientId);
                if (ing == null) continue;

                var resolvedName = (ing.GlobalName ?? "").Trim();
                if (resolvedName.Length == 0) continue;

                var key = (aLower, ing.IngredientId);

                var sev = oh.Severity.HasValue ? Map(oh.Severity.Value) : AllergenResolver.Severity.Low;
                var mayContain = oh.MayContain ?? false;
                var cross = oh.CrossContactRisk ?? false;
                var proc = oh.ProcessingAid ?? false;

                if (!hitMap.TryGetValue(key, out var dto))
                {
                    hitMap[key] = new IngredientAlertDetailDto
                    {
                        IngredientId = ing.IngredientId,
                        IngredientName = resolvedName,
                        Source = "Override",
                        SeverityResolved = SevToString(sev),
                        MayContain = mayContain,
                        CrossContactRisk = cross,
                        ProcessingAid = proc,
                        IsTraceOnly = false
                    };
                }
                else
                {
                    dto.Source = "Override";

                    var cur = ParseSeverity(dto.SeverityResolved);
                    if (sev > cur) dto.SeverityResolved = SevToString(sev);

                    dto.MayContain |= mayContain;
                    dto.CrossContactRisk |= cross;
                    dto.ProcessingAid |= proc;
                }
            }

            // ---------------- 3) Resolver fallback (only if DB has zero hits for that allergy) ----------------
            foreach (var root in roots)
            {
                var rootKey = root.Trim();
                if (rootKey.Length == 0) continue;

                var rootL = rootKey.ToLowerInvariant();
                if (hitMap.Keys.Any(k => k.allergyLower == rootL)) continue;

                var expanded = _resolver.ExpandWithSeverity(new[] { rootKey });

                foreach (var ing in recipeIngredients)
                {
                    var resolvedName = (ing.GlobalName ?? "").Trim();
                    if (resolvedName.Length == 0) continue;

                    var ingNorm = AllergenTextUtils.NormKey(resolvedName);

                    AllergenResolver.Severity best = AllergenResolver.Severity.Low;
                    var matched = false;

                    foreach (var (term, sev) in expanded)
                    {
                        var tNorm = AllergenTextUtils.NormKey(term);
                        if (string.IsNullOrWhiteSpace(tNorm)) continue;

                        if (ingNorm.Contains(tNorm) || tNorm.Contains(ingNorm))
                        {
                            matched = true;
                            if (sev > best) best = sev;
                        }
                    }

                    if (!matched) continue;

                    var key = (rootL, ing.IngredientId);
                    hitMap[key] = new IngredientAlertDetailDto
                    {
                        IngredientId = ing.IngredientId,
                        IngredientName = resolvedName,
                        Source = "Resolver",
                        SeverityResolved = SevToString(best),
                        MayContain = false,
                        CrossContactRisk = false,
                        ProcessingAid = false,
                        IsTraceOnly = false
                    };
                }
            }

            // ---------------- Apply per-allergen user preferences ----------------
            var keysToRemove = hitMap
                .Where(kv =>
                {
                    if (!allergenPrefs.TryGetValue(kv.Key.allergyLower, out var pref)) return false;
                    var hitSev = ParseSeverity(kv.Value.SeverityResolved);
                    if (hitSev < Map(pref.MinSeverity)) return true;
                    if (!pref.IncludeTrace && kv.Value.MayContain) return true;
                    return false;
                })
                .Select(kv => kv.Key)
                .ToList();
            foreach (var k in keysToRemove) hitMap.Remove(k);

            // ---------------- Build response ----------------
            var grouped = hitMap
                .GroupBy(kv => kv.Key.allergyLower)
                .Select(g =>
                {
                    var ings = g.Select(x => x.Value).ToList();

                    var highest = ings
                        .Select(x => ParseSeverity(x.SeverityResolved))
                        .DefaultIfEmpty(AllergenResolver.Severity.Low)
                        .Max();

                    var display = displayByLower.TryGetValue(g.Key, out var name) ? name : g.Key;

                    return new AllergyAlertDto
                    {
                        AllergenType = display,
                        HighestSeverity = SevToString(highest),
                        Ingredients = ings
                    };
                })
                .OrderBy(x => x.AllergenType)
                .ToList();

            return new RecipeAlertsResponse
            {
                RecipeId = recipeId,
                Alerts = grouped
            };
        }

        /// <inheritdoc/>
        public async Task<Dictionary<Guid, RecipeAlertsResponse>> GetAlertsForRecipesAsync(
            IReadOnlyList<Guid> recipeIds,
            Guid userId,
            CancellationToken ct = default)
        {
            // Initialise a result entry for every requested recipe so callers
            // always get back a key — even for recipes with no ingredients.
            var result = new Dictionary<Guid, RecipeAlertsResponse>();
            foreach (var id in recipeIds)
                result[id] = new RecipeAlertsResponse { RecipeId = id, Alerts = new List<AllergyAlertDto>() };

            if (recipeIds.Count == 0)
                return result;

            // ── Query 1: user allergens + preferences (ONCE for the whole batch) ─
            var userAllergenRows = await (
                from ua in _db.UserAllergies.AsNoTracking()
                join a in _db.Allergies.AsNoTracking() on ua.AllergyId equals a.Id
                where ua.UserId == userId && ua.IsEnabled
                select new { AllergenName = a.Name, ua.MinSeverity, ua.IncludeTrace }
            ).ToListAsync(ct);

            var roots = userAllergenRows
                .Where(x => !string.IsNullOrWhiteSpace(x.AllergenName))
                .Select(x => x.AllergenName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (roots.Count == 0)
                return result;

            var allergenPrefs = userAllergenRows
                .Where(x => !string.IsNullOrWhiteSpace(x.AllergenName))
                .GroupBy(x => x.AllergenName.Trim().ToLowerInvariant())
                .ToDictionary(
                    g => g.Key,
                    g => (MinSeverity: g.Min(x => x.MinSeverity), IncludeTrace: g.Any(x => x.IncludeTrace))
                );

            var rootLower = roots.Select(x => x.ToLowerInvariant()).Distinct().ToList();
            var displayByLower = roots
                .GroupBy(x => x.Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First().Trim());

            // ── Query 2: all ingredients for all recipes in one shot ──────────
            var allIngredients = await (
                from i in _db.Ingredients.AsNoTracking()
                where recipeIds.Contains(i.RecipeId)
                join g in _db.GlobalIngredients.AsNoTracking() on i.GlobalIngredientId equals g.Id into gj
                from g in gj.DefaultIfEmpty()
                select new
                {
                    RecipeId = i.RecipeId,
                    IngredientId = i.Id,
                    GlobalIngredientId = i.GlobalIngredientId,
                    GlobalName = g != null ? g.Name : null
                }
            ).ToListAsync(ct);

            // Group ingredients by recipe for later per-recipe processing.
            var ingredientsByRecipe = allIngredients
                .GroupBy(x => x.RecipeId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // ── Query 3: GlobalIngredientAllergens for all global ingredient IDs ──
            // GlobalIngredientId on Ingredient is a non-nullable Guid.
            var allGlobalIds = allIngredients
                .Select(x => x.GlobalIngredientId)
                .Distinct()
                .ToList();

            var globalHitRaw = allGlobalIds.Count == 0
                ? new List<(Guid GlobalIngredientId, string? AllergenType, AllergenSeverity Severity,
                             bool MayContain, bool CrossContactRisk, bool ProcessingAid)>()
                : (await (
                    from ga in _db.GlobalIngredientAllergens.AsNoTracking()
                    where allGlobalIds.Contains(ga.GlobalIngredientId)
                    select new
                    {
                        ga.GlobalIngredientId,
                        ga.AllergenType,
                        ga.Severity,
                        ga.MayContain,
                        ga.CrossContactRisk,
                        ga.ProcessingAid
                    }
                  ).ToListAsync(ct))
                  .Select(x => (x.GlobalIngredientId, x.AllergenType, x.Severity,
                                x.MayContain, x.CrossContactRisk, x.ProcessingAid))
                  .ToList();

            var globalHitsByGlobalId = globalHitRaw
                .GroupBy(x => x.GlobalIngredientId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // ── Query 4: IngredientAllergens (overrides) for all ingredient IDs ──
            var allIngredientIds = allIngredients.Select(x => x.IngredientId).Distinct().ToList();

            var overrideHitRaw = allIngredientIds.Count == 0
                ? new List<(Guid IngredientId, string? AllergenType, AllergenSeverity? Severity,
                             bool? MayContain, bool? CrossContactRisk, bool? ProcessingAid)>()
                : (await (
                    from ia in _db.IngredientAllergens.AsNoTracking()
                    where allIngredientIds.Contains(ia.IngredientId)
                    select new
                    {
                        ia.IngredientId,
                        ia.AllergenType,
                        ia.Severity,
                        ia.MayContain,
                        ia.CrossContactRisk,
                        ia.ProcessingAid
                    }
                  ).ToListAsync(ct))
                  .Select(x => (x.IngredientId, x.AllergenType, x.Severity,
                                x.MayContain, x.CrossContactRisk, x.ProcessingAid))
                  .ToList();

            var overrideHitsByIngredientId = overrideHitRaw
                .GroupBy(x => x.IngredientId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // ── Step 5: per-recipe in-memory assembly (identical logic to single method) ──
            foreach (var recipeId in recipeIds)
            {
                if (!ingredientsByRecipe.TryGetValue(recipeId, out var recipeIngredients)
                    || recipeIngredients.Count == 0)
                {
                    // already initialised with empty Alerts above
                    continue;
                }

                var hitMap = new Dictionary<(string allergyLower, Guid ingredientId), IngredientAlertDetailDto>();

                // 1) GlobalIngredientAllergens
                foreach (var ing in recipeIngredients)
                {
                    if (!globalHitsByGlobalId.TryGetValue(ing.GlobalIngredientId, out var ghList)) continue;

                    foreach (var gh in ghList)
                    {
                        var aType = (gh.AllergenType ?? "").Trim();
                        if (aType.Length == 0) continue;

                        var aLower = aType.ToLowerInvariant();
                        if (!rootLower.Contains(aLower)) continue;

                        var resolvedName = (ing.GlobalName ?? "").Trim();
                        if (resolvedName.Length == 0) continue;

                        var key = (aLower, ing.IngredientId);
                        var sev = Map(gh.Severity);

                        if (!hitMap.TryGetValue(key, out var dto))
                        {
                            hitMap[key] = new IngredientAlertDetailDto
                            {
                                IngredientId = ing.IngredientId,
                                IngredientName = resolvedName,
                                Source = "Global",
                                SeverityResolved = SevToString(sev),
                                MayContain = gh.MayContain,
                                CrossContactRisk = gh.CrossContactRisk,
                                ProcessingAid = gh.ProcessingAid,
                                IsTraceOnly = false
                            };
                        }
                        else
                        {
                            var cur = ParseSeverity(dto.SeverityResolved);
                            if (sev > cur) dto.SeverityResolved = SevToString(sev);
                            dto.MayContain |= gh.MayContain;
                            dto.CrossContactRisk |= gh.CrossContactRisk;
                            dto.ProcessingAid |= gh.ProcessingAid;
                        }
                    }
                }

                // 2) IngredientAllergens overrides
                foreach (var ing in recipeIngredients)
                {
                    if (!overrideHitsByIngredientId.TryGetValue(ing.IngredientId, out var ohList)) continue;

                    foreach (var oh in ohList)
                    {
                        var aType = (oh.AllergenType ?? "").Trim();
                        if (aType.Length == 0) continue;

                        var aLower = aType.ToLowerInvariant();
                        if (!rootLower.Contains(aLower)) continue;

                        var resolvedName = (ing.GlobalName ?? "").Trim();
                        if (resolvedName.Length == 0) continue;

                        var key = (aLower, ing.IngredientId);
                        var sev = oh.Severity.HasValue ? Map(oh.Severity.Value) : AllergenResolver.Severity.Low;
                        var mayContain = oh.MayContain ?? false;
                        var cross = oh.CrossContactRisk ?? false;
                        var proc = oh.ProcessingAid ?? false;

                        if (!hitMap.TryGetValue(key, out var dto))
                        {
                            hitMap[key] = new IngredientAlertDetailDto
                            {
                                IngredientId = ing.IngredientId,
                                IngredientName = resolvedName,
                                Source = "Override",
                                SeverityResolved = SevToString(sev),
                                MayContain = mayContain,
                                CrossContactRisk = cross,
                                ProcessingAid = proc,
                                IsTraceOnly = false
                            };
                        }
                        else
                        {
                            dto.Source = "Override";
                            var cur = ParseSeverity(dto.SeverityResolved);
                            if (sev > cur) dto.SeverityResolved = SevToString(sev);
                            dto.MayContain |= mayContain;
                            dto.CrossContactRisk |= cross;
                            dto.ProcessingAid |= proc;
                        }
                    }
                }

                // 3) Resolver fallback — only for allergen roots with zero DB hits for this recipe
                foreach (var root in roots)
                {
                    var rootKey = root.Trim();
                    if (rootKey.Length == 0) continue;

                    var rootL = rootKey.ToLowerInvariant();
                    if (hitMap.Keys.Any(k => k.allergyLower == rootL)) continue;

                    var expanded = _resolver.ExpandWithSeverity(new[] { rootKey });

                    foreach (var ing in recipeIngredients)
                    {
                        var resolvedName = (ing.GlobalName ?? "").Trim();
                        if (resolvedName.Length == 0) continue;

                        var ingNorm = AllergenTextUtils.NormKey(resolvedName);

                        AllergenResolver.Severity best = AllergenResolver.Severity.Low;
                        var matched = false;

                        foreach (var (term, sev) in expanded)
                        {
                            var tNorm = AllergenTextUtils.NormKey(term);
                            if (string.IsNullOrWhiteSpace(tNorm)) continue;

                            if (ingNorm.Contains(tNorm) || tNorm.Contains(ingNorm))
                            {
                                matched = true;
                                if (sev > best) best = sev;
                            }
                        }

                        if (!matched) continue;

                        hitMap[(rootL, ing.IngredientId)] = new IngredientAlertDetailDto
                        {
                            IngredientId = ing.IngredientId,
                            IngredientName = resolvedName,
                            Source = "Resolver",
                            SeverityResolved = SevToString(best),
                            MayContain = false,
                            CrossContactRisk = false,
                            ProcessingAid = false,
                            IsTraceOnly = false
                        };
                    }
                }

                // Apply per-allergen user preferences (MinSeverity / IncludeTrace)
                var batchKeysToRemove = hitMap
                    .Where(kv =>
                    {
                        if (!allergenPrefs.TryGetValue(kv.Key.allergyLower, out var pref)) return false;
                        var hitSev = ParseSeverity(kv.Value.SeverityResolved);
                        if (hitSev < Map(pref.MinSeverity)) return true;
                        if (!pref.IncludeTrace && kv.Value.MayContain) return true;
                        return false;
                    })
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (var k in batchKeysToRemove) hitMap.Remove(k);

                // Build response for this recipe
                var grouped = hitMap
                    .GroupBy(kv => kv.Key.allergyLower)
                    .Select(g =>
                    {
                        var ings = g.Select(x => x.Value).ToList();
                        var highest = ings
                            .Select(x => ParseSeverity(x.SeverityResolved))
                            .DefaultIfEmpty(AllergenResolver.Severity.Low)
                            .Max();
                        var display = displayByLower.TryGetValue(g.Key, out var name) ? name : g.Key;
                        return new AllergyAlertDto
                        {
                            AllergenType = display,
                            HighestSeverity = SevToString(highest),
                            Ingredients = ings
                        };
                    })
                    .OrderBy(x => x.AllergenType)
                    .ToList();

                result[recipeId] = new RecipeAlertsResponse { RecipeId = recipeId, Alerts = grouped };
            }

            return result;
        }
    }
}
