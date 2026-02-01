using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    public class RecipeSafetyService : IRecipeSafetyService
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<RecipeSafetyService> _logger;

        public RecipeSafetyService(
            PlaterlyDbContext db,
            ILogger<RecipeSafetyService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<RecipeSafetyAuditResponse> AuditRecipeAsync(
            RecipeSafetyAuditRequest request,
            CancellationToken ct = default)
        {
            var allergenFlags = new List<SafetyFlagDto>();
            var prohibitedFlags = new List<SafetyFlagDto>();
            var crossContactFlags = new List<SafetyFlagDto>();

            foreach (var ingredientName in request.Ingredients)
            {
                var lowerName = ingredientName.Trim().ToLower();

                // Check prohibited ingredients (keyword matching)
                var prohibited = await _db.ProhibitedIngredients
                    .Where(p => p.IsActive && (
                        (p.MatchType == Models.MatchType.Exact && p.Keyword.ToLower() == lowerName) ||
                        (p.MatchType == Models.MatchType.Contains && lowerName.Contains(p.Keyword.ToLower())) ||
                        (p.MatchType == Models.MatchType.StartsWith && lowerName.StartsWith(p.Keyword.ToLower()))
                    ))
                    .FirstOrDefaultAsync(ct);

                if (prohibited != null)
                {
                    prohibitedFlags.Add(new SafetyFlagDto
                    {
                        IngredientName = ingredientName,
                        FlagType = "Prohibited",
                        Detail = prohibited.Reason ?? "This ingredient is prohibited on the platform",
                        Severity = prohibited.Severity >= Models.ViolationSeverity.Block ? "High" : "Medium"
                    });
                    continue;
                }

                // Find the global ingredient
                var ingredient = await _db.GlobalIngredients
                    .Where(g => g.Name.ToLower() == lowerName)
                    .FirstOrDefaultAsync(ct);

                if (ingredient == null) continue;

                // Check allergens
                var allergens = await _db.GlobalIngredientAllergens
                    .Where(ga => ga.GlobalIngredientId == ingredient.Id)
                    .ToListAsync(ct);

                foreach (var allergen in allergens)
                {
                    if (allergen.CrossContactRisk)
                    {
                        crossContactFlags.Add(new SafetyFlagDto
                        {
                            IngredientName = ingredientName,
                            FlagType = "CrossContact",
                            Detail = $"Cross-contact risk for {allergen.AllergenType}",
                            Severity = allergen.Severity.ToString()
                        });
                    }

                    allergenFlags.Add(new SafetyFlagDto
                    {
                        IngredientName = ingredientName,
                        FlagType = "Allergen",
                        Detail = allergen.MayContain
                            ? $"May contain {allergen.AllergenType}"
                            : $"Contains {allergen.AllergenType}",
                        Severity = allergen.Severity.ToString()
                    });
                }
            }

            var totalFlags = allergenFlags.Count + prohibitedFlags.Count + crossContactFlags.Count;
            var flaggedIngredients = allergenFlags.Select(f => f.IngredientName)
                .Union(prohibitedFlags.Select(f => f.IngredientName))
                .Union(crossContactFlags.Select(f => f.IngredientName))
                .Distinct().Count();

            var overallRisk = prohibitedFlags.Count > 0 ? "High"
                : allergenFlags.Any(f => f.Severity == "High") ? "High"
                : totalFlags > 5 ? "Medium"
                : totalFlags > 0 ? "Low"
                : "None";

            return new RecipeSafetyAuditResponse
            {
                TotalIngredients = request.Ingredients.Count,
                FlaggedIngredients = flaggedIngredients,
                OverallRisk = overallRisk,
                AllergenFlags = allergenFlags,
                ProhibitedFlags = prohibitedFlags,
                CrossContactFlags = crossContactFlags
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
                    new RecipeSafetyAuditRequest { Ingredients = recipe.Ingredients }, ct);

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
                TotalRecipes = request.Recipes.Count,
                RecipesWithFlags = recipesWithFlags,
                Results = results
            };
        }
    }
}
