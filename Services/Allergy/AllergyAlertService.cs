using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Services;
using Platerly.Api.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Platerly.Api.Services.Allergy
{
    public class AllergyAlertService : IAllergyAlertService
    {
        private readonly IDbContextFactory<PlaterlyDbContext> _dbFactory;
        private readonly IAllergenResolver _resolver;

        public AllergyAlertService(
            IDbContextFactory<PlaterlyDbContext> dbFactory,
            IAllergenResolver resolver)
        {
            _dbFactory = dbFactory;
            _resolver = resolver;
        }

        private static string NormKey(string s)
            => string.Join(" ",
                (s ?? "")
                    .Trim()
                    .ToLowerInvariant()
                    .Replace("\t", " ")
                    .Replace("\n", " ")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            );

        private static AllergenResolver.Severity ParseSeverity(string? s)
    => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "high" => AllergenResolver.Severity.High,
        "medium" => AllergenResolver.Severity.Medium,
        _ => AllergenResolver.Severity.Low
    };


        private static string SevToString(AllergenResolver.Severity s)
    => s == AllergenResolver.Severity.High ? "High"
     : s == AllergenResolver.Severity.Medium ? "Medium"
     : "Low";


        private static AllergenResolver.Severity Map(AllergenSeverity s)
            => s switch
            {
                AllergenSeverity.High => AllergenResolver.Severity.High,
                AllergenSeverity.Medium => AllergenResolver.Severity.Medium,
                _ => AllergenResolver.Severity.Low
            };

        public async Task<RecipeAlertsResponse> GetAlertsForRecipeAsync(Guid recipeId, Guid userId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // ---------------- user allergen types ----------------
            // Your mapping tables use AllergenType (string), so we resolve the user's selected allergy names into strings.
            var userAllergenTypes = await (
                from ua in db.UserAllergies.AsNoTracking()
                join a in db.Allergies.AsNoTracking() on ua.AllergyId equals a.Id
                where ua.UserId == userId
                select a.Name
            ).ToListAsync(ct);

            var roots = userAllergenTypes
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (roots.Count == 0)
            {
                return new RecipeAlertsResponse { RecipeId = recipeId, Alerts = new List<AllergyAlertDto>() };
            }

            // For EF-friendly matching: lower-case bucket keys
            var rootLower = roots.Select(x => x.ToLowerInvariant()).Distinct().ToList();
            var displayByLower = roots
                .GroupBy(x => x.Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First().Trim());

            // ---------------- recipe ingredients (global join; Ingredient.Name is NotMapped) ----------------
            var recipeIngredients = await (
                from i in db.Ingredients.AsNoTracking()
                where i.RecipeId == recipeId
                join g in db.GlobalIngredients.AsNoTracking() on i.GlobalIngredientId equals g.Id into gj
                from g in gj.DefaultIfEmpty()
                select new
                {
                    IngredientId = i.Id,
                    GlobalIngredientId = i.GlobalIngredientId, // non-nullable Guid
                    GlobalName = g != null ? g.Name : null
                }
            ).ToListAsync(ct);

            if (recipeIngredients.Count == 0)
            {
                return new RecipeAlertsResponse { RecipeId = recipeId, Alerts = new List<AllergyAlertDto>() };
            }

            // Key: (allergenTypeLower, ingredientId)
            var hitMap = new Dictionary<(string allergyLower, Guid ingredientId), IngredientAlertDetailDto>();

            // ---------------- 1) GlobalIngredientAllergens ----------------
            var globalIds = recipeIngredients.Select(x => x.GlobalIngredientId).Distinct().ToList();

            var globalHits = await (
                from ga in db.GlobalIngredientAllergens.AsNoTracking()
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
                if (!rootLower.Contains(aLower)) continue; // only what the user selected

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
                from ia in db.IngredientAllergens.AsNoTracking()
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

                // Nullable override fields
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
                    // Override wins on source, but we still keep max severity and OR flags.
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

                var alreadyHasAny = hitMap.Keys.Any(k => k.allergyLower == rootL);
                if (alreadyHasAny) continue;

                var expanded = _resolver.ExpandWithSeverity(new[] { rootKey });

                foreach (var ing in recipeIngredients)
                {
                    var resolvedName = (ing.GlobalName ?? "").Trim();
                    if (resolvedName.Length == 0) continue;

                    var ingNorm = NormKey(resolvedName);

                    AllergenResolver.Severity best = AllergenResolver.Severity.Low;
                    var matched = false;

                    foreach (var (term, sev) in expanded)
                    {
                        var tNorm = NormKey(term);
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
    }
}
