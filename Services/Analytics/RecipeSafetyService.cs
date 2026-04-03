using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;
using Platerly.Api.Services.Ingredients;

namespace Platerly.Api.Services.Analytics
{
    /// <summary>
    /// B2B Phase 2 safety layer — audits a list of ingredient names for allergen presence,
    /// prohibited ingredients, and cross-contact risk; optionally scoped to an allergen profile filter.
    /// Depends on <see cref="PlaterlyDbContext"/> and <see cref="IIngredientSubstitutionService"/>.
    ///
    /// Audit pipeline per ingredient:
    /// <list type="number">
    ///   <item>Prohibited check — matched against active <c>ProhibitedIngredients</c> rows by
    ///     Exact, Contains, and StartsWith rules (Regex excluded for performance).</item>
    ///   <item>GlobalIngredient lookup — exact name match, then progressive word-drop fallback
    ///     (drops leading words iteratively, minimum 3-character candidate) to handle adjective
    ///     prefixes from external recipes (e.g., "extra-lean ground beef" → "ground beef" → "beef").</item>
    ///   <item>Allergen flag — when a matching GlobalIngredient has allergen rows, the allergen
    ///     types are compared against the optional allergen profile filter; when no filter is
    ///     supplied, all allergens are reported.</item>
    ///   <item>Cross-contact flag — reported separately from definite allergen presence
    ///     (<c>MayContain = true</c> or <c>CrossContactRisk = true</c>).</item>
    ///   <item>Substitution suggestions — when an allergen profile filter is provided,
    ///     <see cref="IIngredientSubstitutionService"/> is queried for safe alternatives.</item>
    /// </list>
    ///
    /// Every response includes a mandatory clinical disclaimer.
    /// </summary>
    public class RecipeSafetyService : IRecipeSafetyService
    {
        private readonly PlaterlyDbContext _db;
        private readonly IIngredientSubstitutionService _substitutions;
        private readonly ILogger<RecipeSafetyService> _logger;

        private const string Disclaimer =
            "This audit is generated from a crowdsourced and curated ingredient database. " +
            "Results are informational only and do not constitute medical or dietary advice. " +
            "Always verify allergen safety with a qualified healthcare professional. " +
            "Recipzee accepts no liability for decisions made based on this output.";

        public RecipeSafetyService(
            PlaterlyDbContext db,
            IIngredientSubstitutionService substitutions,
            ILogger<RecipeSafetyService> logger)
        {
            _db = db;
            _substitutions = substitutions;
            _logger = logger;
        }

        /// <summary>
        /// Audits a list of ingredient names for allergen and prohibited-ingredient flags.
        /// An optional allergen profile filter scopes allergen detection to specific types and
        /// enables allergen-safe substitution suggestions per flagged ingredient.
        /// Returns per-ingredient flags grouped by type (Allergen, Prohibited, CrossContact),
        /// an overall safety rating (Safe / Review / Unsafe), and a mandatory disclaimer.
        /// </summary>
        /// <param name="request">Ingredient names, optional allergen profile filter, and optional profile name.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Full safety audit response with flags, summary counts, rating, and disclaimer.</returns>
        public async Task<RecipeSafetyAuditResponse> AuditRecipeAsync(
            RecipeSafetyAuditRequest request,
            CancellationToken ct = default)
        {
            // Normalise the allergen profile filter (null = no filter, empty list = no filter)
            var profileFilter = request.AllergenProfile?
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a.Trim().ToLowerInvariant())
                .ToHashSet();

            if (profileFilter != null && profileFilter.Count == 0)
                profileFilter = null;

            var allergenFlags    = new List<SafetyFlagDto>();
            var prohibitedFlags  = new List<SafetyFlagDto>();
            var crossContactFlags = new List<SafetyFlagDto>();
            var flaggedNames     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var ingredientName in request.Ingredients)
            {
                var lowerName = ingredientName.Trim().ToLower();

                // ── Prohibited check ────────────────────────────────────────
                var prohibited = await _db.ProhibitedIngredients
                    .Where(p => p.IsActive && (
                        (p.MatchType == Models.MatchType.Exact     && p.Keyword.ToLower() == lowerName) ||
                        (p.MatchType == Models.MatchType.Contains  && lowerName.Contains(p.Keyword.ToLower())) ||
                        (p.MatchType == Models.MatchType.StartsWith && lowerName.StartsWith(p.Keyword.ToLower()))
                    ))
                    .FirstOrDefaultAsync(ct);

                if (prohibited != null)
                {
                    var flag = new SafetyFlagDto
                    {
                        IngredientName = ingredientName,
                        FlagType       = "Prohibited",
                        Detail         = prohibited.Reason ?? "This ingredient is prohibited on the platform",
                        Severity       = prohibited.Severity >= Models.ViolationSeverity.Block ? "High" : "Medium"
                    };

                    // Fetch substitutes when profile provided
                    if (profileFilter != null)
                        flag.Substitutions = await FetchSubstitutionsAsync(ingredientName, profileFilter, ct);

                    prohibitedFlags.Add(flag);
                    flaggedNames.Add(ingredientName);
                    continue;
                }

                // ── GlobalIngredient lookup ──────────────────────────────────
                // Try exact match first, then progressively drop leading words to
                // handle quality/prep adjective prefixes from web recipes:
                // "extra-lean ground beef" → "ground beef" → "beef"
                // "fresh blueberries"      → "blueberries"
                var ingredient = await _db.GlobalIngredients
                    .Where(g => g.Name.ToLower() == lowerName)
                    .FirstOrDefaultAsync(ct);

                if (ingredient == null)
                {
                    var dropWords = lowerName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    for (int drop = 1; drop < dropWords.Length && ingredient == null; drop++)
                    {
                        var candidate = string.Join(" ", dropWords.Skip(drop));
                        if (candidate.Length < 3) break;
                        ingredient = await _db.GlobalIngredients
                            .Where(g => g.Name.ToLower() == candidate)
                            .FirstOrDefaultAsync(ct);
                    }
                }

                if (ingredient == null) continue;

                // ── Allergen flags from GlobalIngredientAllergens ────────────
                var allergens = await _db.GlobalIngredientAllergens
                    .Where(ga => ga.GlobalIngredientId == ingredient.Id)
                    .ToListAsync(ct);

                foreach (var allergen in allergens)
                {
                    var allergenLower = allergen.AllergenType?.ToLowerInvariant() ?? "";

                    // Apply AllergenProfile filter — skip allergens not in profile
                    if (profileFilter != null && !profileFilter.Any(p =>
                            allergenLower.Contains(p) || p.Contains(allergenLower)))
                        continue;

                    if (allergen.CrossContactRisk)
                    {
                        crossContactFlags.Add(new SafetyFlagDto
                        {
                            IngredientName = ingredientName,
                            FlagType       = "CrossContact",
                            Detail         = $"Cross-contact risk for {allergen.AllergenType}",
                            Severity       = allergen.Severity.ToString()
                        });
                    }

                    allergenFlags.Add(new SafetyFlagDto
                    {
                        IngredientName = ingredientName,
                        FlagType       = "Allergen",
                        Detail         = allergen.MayContain
                            ? $"May contain {allergen.AllergenType}"
                            : $"Contains {allergen.AllergenType}",
                        Severity       = allergen.Severity.ToString()
                    });

                    flaggedNames.Add(ingredientName);
                }

                // ── Nickel classification check (reads GlobalIngredients directly) ──
                // NickelDataSeeder writes nickel data to GlobalIngredients fields, not
                // to GlobalIngredientAllergens rows. We bridge that gap here so that
                // AllergenProfile=["nickel"] correctly surfaces MODERATE and HIGH
                // nickel ingredients that the structured allergen table doesn't cover.
                bool nickelInScope = profileFilter == null || profileFilter.Contains("nickel");
                if (nickelInScope &&
                    (ingredient.NickelClassification == NickelClassification.Moderate ||
                     ingredient.NickelClassification == NickelClassification.High))
                {
                    // Skip if a nickel allergen row was already added above (avoid duplication)
                    bool alreadyFlagged = allergenFlags.Any(f =>
                        f.IngredientName.Equals(ingredientName, StringComparison.OrdinalIgnoreCase) &&
                        f.Detail.IndexOf("ickel", StringComparison.OrdinalIgnoreCase) >= 0);

                    if (!alreadyFlagged)
                    {
                        var nickelSeverity = ingredient.NickelClassification == NickelClassification.High
                            ? "High" : "Medium";

                        var nickelDetail = ingredient.NickelMicrogramsPerServing.HasValue
                            ? $"Contains {ingredient.NickelClassification} nickel " +
                              $"({ingredient.NickelMicrogramsPerServing:F1} μg/serving, " +
                              $"{ingredient.NickelServingSizeGrams}g serving)"
                            : $"Contains {ingredient.NickelClassification} nickel";

                        allergenFlags.Add(new SafetyFlagDto
                        {
                            IngredientName = ingredientName,
                            FlagType       = "Allergen",
                            Detail         = nickelDetail,
                            Severity       = nickelSeverity
                        });

                        flaggedNames.Add(ingredientName);
                    }
                }
            }

            // ── Substitutions for each allergen-flagged ingredient ───────────
            // Group by ingredient name so we only query once per ingredient
            if (profileFilter != null)
            {
                var uniqueFlagged = allergenFlags
                    .Select(f => f.IngredientName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var subMap = new Dictionary<string, List<SafeSubstituteSuggestion>>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in uniqueFlagged)
                {
                    subMap[name] = await FetchSubstitutionsAsync(name, profileFilter, ct);
                }

                foreach (var flag in allergenFlags)
                {
                    if (subMap.TryGetValue(flag.IngredientName, out var subs))
                        flag.Substitutions = subs;
                }

                foreach (var flag in crossContactFlags)
                {
                    if (subMap.TryGetValue(flag.IngredientName, out var subs))
                        flag.Substitutions = subs;
                }
            }

            // ── Safe ingredients (no flags) ──────────────────────────────────
            var safeIngredients = request.Ingredients
                .Where(i => !flaggedNames.Contains(i))
                .ToList();

            // ── Overall risk ─────────────────────────────────────────────────
            var totalFlags = allergenFlags.Count + prohibitedFlags.Count + crossContactFlags.Count;
            var overallRisk = prohibitedFlags.Count > 0                          ? "High"
                : allergenFlags.Any(f => f.Severity is "High" or "Critical")    ? "High"
                : totalFlags > 5                                                  ? "Medium"
                : totalFlags > 0                                                  ? "Low"
                : "None";

            return new RecipeSafetyAuditResponse
            {
                TotalIngredients   = request.Ingredients.Count,
                FlaggedIngredients = flaggedNames.Count,
                OverallRiskLevel   = overallRisk,
                AllergenFlags      = allergenFlags,
                ProhibitedFlags    = prohibitedFlags,
                CrossContactFlags  = crossContactFlags,
                SafeIngredients    = safeIngredients,
                Disclaimer         = Disclaimer
            };
        }

        public async Task<BatchAuditResponse> BatchAuditAsync(
            BatchAuditRequest request,
            CancellationToken ct = default)
        {
            var results = new List<BatchAuditRecipeResult>();
            int recipesWithFlags = 0;

            foreach (var recipe in request.Recipes)
            {
                var audit = await AuditRecipeAsync(
                    new RecipeSafetyAuditRequest
                    {
                        Ingredients    = recipe.Ingredients,
                        AllergenProfile = recipe.AllergenProfile
                    }, ct);

                if (audit.FlaggedIngredients > 0)
                    recipesWithFlags++;

                results.Add(new BatchAuditRecipeResult
                {
                    Label = recipe.Label,
                    Audit = audit
                });
            }

            return new BatchAuditResponse
            {
                TotalRecipes       = request.Recipes.Count,
                RecipesWithFlags   = recipesWithFlags,
                Results            = results
            };
        }

        // ── Private helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Fetch the top 2 substitutes for a given ingredient name,
        /// excluding substitutes that themselves contain allergens in the profile.
        /// </summary>
        private async Task<List<SafeSubstituteSuggestion>> FetchSubstitutionsAsync(
            string ingredientName,
            HashSet<string> profileFilter,
            CancellationToken ct)
        {
            var subs = await _substitutions.GetSubstitutionsForIngredientNameAsync(
                ingredientName, userAllergyIds: null, ct);

            bool filterNickel = profileFilter.Contains("nickel");
            var result = new List<SafeSubstituteSuggestion>();

            foreach (var sub in subs)
            {
                if (result.Count >= 2) break;

                // Skip substitutes whose name directly matches a profiled allergen term
                var subNameLower = sub.SubstituteIngredientName.ToLowerInvariant();
                if (profileFilter.Any(p => subNameLower.Contains(p) || p.Contains(subNameLower)))
                    continue;

                // When nickel is in profile, look up the substitute's nickel classification
                // and exclude HIGH-nickel substitutes — patient safety requirement
                if (filterNickel)
                {
                    var subNickelClass = await _db.GlobalIngredients
                        .Where(g => g.Name.ToLower() == subNameLower ||
                                    g.NormalizedName == subNameLower)
                        .Select(g => (NickelClassification?)g.NickelClassification)
                        .FirstOrDefaultAsync(ct);

                    if (subNickelClass == NickelClassification.High)
                        continue;

                    result.Add(new SafeSubstituteSuggestion
                    {
                        SubstituteName   = sub.SubstituteIngredientName,
                        Reason           = sub.Reason,
                        ConversionRatio  = sub.ConversionRatio,
                        ConfidenceScore  = sub.ConfidenceScore,
                        IsVerified       = sub.IsVerified,
                        Notes            = sub.Notes,
                        NickelSafetyNote = subNickelClass == NickelClassification.Moderate
                            ? $"{sub.SubstituteIngredientName} is MODERATE nickel. " +
                              "Monitor daily total against the 150 μg/day threshold."
                            : sub.NickelSafetyNote
                    });
                }
                else
                {
                    result.Add(new SafeSubstituteSuggestion
                    {
                        SubstituteName   = sub.SubstituteIngredientName,
                        Reason           = sub.Reason,
                        ConversionRatio  = sub.ConversionRatio,
                        ConfidenceScore  = sub.ConfidenceScore,
                        IsVerified       = sub.IsVerified,
                        Notes            = sub.Notes,
                        NickelSafetyNote = sub.NickelSafetyNote
                    });
                }
            }

            return result;
        }
    }
}
