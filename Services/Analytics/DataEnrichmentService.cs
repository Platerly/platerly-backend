using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Analytics
{
    /// <summary>
    /// B2B data layer — enriches ingredient and recipe data with allergen, dietary, substitution,
    /// and inferred dietary-tag information from the Recipzee database.
    /// Depends on <see cref="PlaterlyDbContext"/>.
    ///
    /// Used by B2B partners to augment their own product or recipe catalogues with
    /// structured safety and dietary information without building their own ingredient database.
    /// </summary>
    public class DataEnrichmentService : IDataEnrichmentService
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<DataEnrichmentService> _logger;

        public DataEnrichmentService(
            PlaterlyDbContext db,
            ILogger<DataEnrichmentService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Enriches a list of ingredient names with allergen types, category, dietary flags,
        /// and top substitutes from the <c>GlobalIngredients</c> database.
        /// Unmatched names are returned with <c>Found=false</c>.
        /// </summary>
        /// <param name="request">List of ingredient name strings to look up.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>One <see cref="EnrichedIngredientDto"/> per input name, matched or not.</returns>
        public async Task<IngredientEnrichmentResponse> EnrichIngredientsAsync(
            IngredientEnrichmentRequest request,
            CancellationToken ct = default)
        {
            var results = new List<EnrichedIngredientDto>();
            int matchedCount = 0;

            foreach (var name in request.IngredientNames)
            {
                var lowerName = name.Trim().ToLower();

                var ingredient = await _db.GlobalIngredients
                    .Include(g => g.Category)
                    .Where(g => g.Name.ToLower() == lowerName)
                    .FirstOrDefaultAsync(ct);

                if (ingredient == null)
                {
                    results.Add(new EnrichedIngredientDto
                    {
                        InputName = name,
                        Found = false
                    });
                    continue;
                }

                matchedCount++;

                // Get allergens
                var allergens = await _db.GlobalIngredientAllergens
                    .Where(ga => ga.GlobalIngredientId == ingredient.Id)
                    .Select(ga => ga.AllergenType)
                    .Distinct()
                    .ToListAsync(ct);

                // Get dietary flags from recipes using this ingredient
                var dietaryFlags = await _db.Ingredients
                    .Where(i => i.GlobalIngredientId == ingredient.Id)
                    .SelectMany(i => i.Recipe!.RecipeTags)
                    .Where(rt => rt.Tag != null)
                    .Select(rt => rt.Tag!.Name)
                    .Distinct()
                    .Take(10)
                    .ToListAsync(ct);

                // Get top substitutes
                var substitutes = await _db.IngredientSubstitutions
                    .Include(s => s.SubstituteIngredient)
                    .Where(s => s.IsActive && s.OriginalIngredientId == ingredient.Id)
                    .OrderByDescending(s => s.ConfidenceScore)
                    .Take(3)
                    .Select(s => new EnrichedSubstituteDto
                    {
                        Name = s.SubstituteIngredient.Name,
                        Reason = s.Reason.ToString(),
                        ConfidenceScore = s.ConfidenceScore
                    })
                    .ToListAsync(ct);

                results.Add(new EnrichedIngredientDto
                {
                    InputName = name,
                    Found = true,
                    GlobalIngredientId = ingredient.Id,
                    MatchedName = ingredient.Name,
                    Category = ingredient.Category?.Name,
                    Allergens = allergens,
                    DietaryFlags = dietaryFlags,
                    TopSubstitutes = substitutes
                });
            }

            return new IngredientEnrichmentResponse
            {
                Ingredients = results,
                MatchedCount = matchedCount,
                TotalCount = request.IngredientNames.Count
            };
        }

        public async Task<RecipeEnrichmentResponse> EnrichRecipeAsync(
            RecipeEnrichmentRequest request,
            CancellationToken ct = default)
        {
            var allergenWarnings = new List<AllergenWarningDto>();
            var categories = new HashSet<string>();
            int recognized = 0;

            foreach (var ingredientName in request.Ingredients)
            {
                var lowerName = ingredientName.Trim().ToLower();

                var ingredient = await _db.GlobalIngredients
                    .Include(g => g.Category)
                    .Where(g => g.Name.ToLower() == lowerName)
                    .FirstOrDefaultAsync(ct);

                if (ingredient == null) continue;
                recognized++;

                if (ingredient.Category != null)
                    categories.Add(ingredient.Category.Name);

                // Check allergens
                var allergens = await _db.GlobalIngredientAllergens
                    .Where(ga => ga.GlobalIngredientId == ingredient.Id)
                    .Select(ga => new AllergenWarningDto
                    {
                        IngredientName = ingredient.Name,
                        AllergenType = ga.AllergenType,
                        Severity = ga.Severity.ToString(),
                        MayContain = ga.MayContain
                    })
                    .ToListAsync(ct);

                allergenWarnings.AddRange(allergens);
            }

            // Estimate dietary tags based on ingredients
            var dietaryTags = await InferDietaryTagsAsync(request.Ingredients, ct);

            // Estimate difficulty based on ingredient count
            var difficulty = request.Ingredients.Count switch
            {
                <= 5 => "Easy",
                <= 10 => "Medium",
                _ => "Hard"
            };

            // Estimate cuisine from region or ingredient patterns
            var cuisine = request.Region ?? "Unknown";

            return new RecipeEnrichmentResponse
            {
                DietaryTags = dietaryTags,
                AllergenWarnings = allergenWarnings,
                EstimatedDifficulty = difficulty,
                EstimatedCuisine = cuisine,
                IngredientCount = request.Ingredients.Count,
                RecognizedIngredients = recognized,
                Categories = categories.ToList()
            };
        }

        private async Task<List<string>> InferDietaryTagsAsync(
            List<string> ingredientNames, CancellationToken ct)
        {
            var tags = new List<string>();
            var lowerNames = ingredientNames.Select(n => n.Trim().ToLower()).ToList();

            // Look up all matched global ingredients
            var ingredients = await _db.GlobalIngredients
                .Where(g => lowerNames.Contains(g.Name.ToLower()))
                .ToListAsync(ct);

            // Check allergen presence for common dietary classifications
            var allergenTypes = await _db.GlobalIngredientAllergens
                .Where(ga => lowerNames.Contains(ga.GlobalIngredient.Name.ToLower()))
                .Select(ga => ga.AllergenType.ToLower())
                .Distinct()
                .ToListAsync(ct);

            if (!allergenTypes.Contains("dairy") && !allergenTypes.Contains("egg") &&
                !allergenTypes.Contains("meat") && !allergenTypes.Contains("fish") &&
                !allergenTypes.Contains("shellfish"))
            {
                tags.Add("Possibly Vegan");
            }
            else if (!allergenTypes.Contains("meat") && !allergenTypes.Contains("fish") &&
                     !allergenTypes.Contains("shellfish"))
            {
                tags.Add("Possibly Vegetarian");
            }

            if (!allergenTypes.Contains("gluten") && !allergenTypes.Contains("wheat"))
            {
                tags.Add("Possibly Gluten-Free");
            }

            if (!allergenTypes.Contains("dairy"))
            {
                tags.Add("Possibly Dairy-Free");
            }

            return tags;
        }
    }
}
