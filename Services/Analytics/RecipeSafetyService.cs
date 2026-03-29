using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Services.Ingredients;

namespace Platerly.Api.Services.Analytics
{
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
                var ingredient = await _db.GlobalIngredients
                    .Where(g => g.Name.ToLower() == lowerName)
                    .FirstOrDefaultAsync(ct);

                if (ingredient == null) continue;

                // ── Allergen flags ───────────────────────────────────────────
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

            var result = new List<SafeSubstituteSuggestion>();

            foreach (var sub in subs)
            {
                if (result.Count >= 2) break;

                // Safety check: skip substitutes whose own name matches the allergen profile
                var subNameLower = sub.SubstituteIngredientName.ToLowerInvariant();
                if (profileFilter.Any(p => subNameLower.Contains(p) || p.Contains(subNameLower)))
                    continue;

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

            return result;
        }
    }
}
