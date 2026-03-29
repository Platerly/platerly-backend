using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// Public ingredient lookup — no authentication required.
    ///
    /// GET /api/public/ingredients/search?q={query}&amp;limit={n}
    ///   Search ingredients by name. Returns name, slug, and nickel classification.
    ///
    /// GET /api/public/ingredients/{slug}
    ///   Full ingredient detail including nickel classification, μg value, confidence level,
    ///   allergen types, and cross-reactivity information.
    ///   Slug is the ingredient's normalised name with spaces replaced by hyphens.
    ///   Example: /api/public/ingredients/spinach
    ///
    /// Data source: Rebelytics R&amp;D Inc., Nickel in Foods Summary Tables v9.1.1, September 2025.
    /// </summary>
    [ApiController]
    [Route("api/public/ingredients")]
    [AllowAnonymous]
    public class PublicIngredientController : ControllerBase
    {
        private readonly PlaterlyDbContext _db;

        public PublicIngredientController(PlaterlyDbContext db)
        {
            _db = db;
        }

        // ── Cross-reactivity knowledge base ───────────────────────────────────
        // Same 12 allergen pairs used by AllergenInteractionService.
        // Stored here as a lightweight static lookup for the public ingredient endpoint.

        private sealed record CrossReactPair(
            string AllergenA,
            string AllergenB,
            string Mechanism,
            int CoSensitisationLikelihood,
            string Significance,
            string Note);

        private static readonly CrossReactPair[] CrossReactivityPairs =
        {
            new("Peanuts", "Tree Nuts",
                "IgE cross-reactivity via shared storage proteins (2S albumins, vicilins)",
                35, "MODERATE",
                "Peanut-allergic individuals have a 25–40% rate of co-sensitisation to tree nuts. Clinical reactivity varies; individual nut species assessment is recommended."),
            new("Tree Nuts", "Peanuts",
                "IgE cross-reactivity via shared storage proteins (2S albumins, vicilins)",
                35, "MODERATE",
                "Tree nut-allergic individuals have a 25–40% rate of co-sensitisation to peanuts. Clinical reactivity varies; individual nut species assessment is recommended."),
            new("Shellfish", "Fish",
                "IgE cross-reactivity via tropomyosin and parvalbumin",
                20, "MODERATE",
                "Shellfish and fish share structurally similar muscle proteins. Co-sensitisation occurs in approximately 20% of shellfish-allergic individuals."),
            new("Fish", "Shellfish",
                "IgE cross-reactivity via tropomyosin and parvalbumin",
                20, "MODERATE",
                "Fish and shellfish share structurally similar muscle proteins. Co-sensitisation occurs in approximately 20% of fish-allergic individuals."),
            new("Milk", "Beef",
                "IgE cross-reactivity via bovine serum albumin (BSA)",
                10, "LOW",
                "A small subset of milk-allergic individuals (approx. 10%) react to beef due to shared bovine serum albumin. Most clinical beef allergy is independent of milk allergy."),
            new("Wheat", "Other Grains",
                "IgE cross-reactivity via gluten and prolamins",
                50, "HIGH",
                "Wheat-allergic individuals frequently cross-react with barley and rye due to structurally homologous prolamins. Oats may be tolerated but are often contaminated. Individual assessment required."),
            new("Sesame", "Tree Nuts",
                "IgE cross-reactivity via 2S albumin seed storage proteins",
                15, "LOW",
                "Sesame shares 2S albumin proteins with some tree nuts. Co-sensitisation is reported but clinical cross-reactivity at the food challenge level is less common."),
            new("Peanuts", "Soy",
                "IgE cross-reactivity via vicilin-class seed storage proteins",
                20, "MODERATE",
                "Peanuts and soy are both legumes sharing vicilin-family proteins. Co-sensitisation is common; clinically significant cross-reactivity occurs in approximately 20% of cases."),
            new("Soy", "Peanuts",
                "IgE cross-reactivity via vicilin-class seed storage proteins",
                20, "MODERATE",
                "Soy and peanuts are both legumes sharing vicilin-family proteins. Co-sensitisation is common; clinically significant cross-reactivity occurs in approximately 20% of cases."),
            new("Milk", "Eggs",
                "Non-immunological co-sensitisation (common in early-onset atopic disease)",
                30, "LOW",
                "Milk and egg allergy frequently co-occur in infants and young children with atopic disease, though the proteins involved are structurally unrelated."),
            new("Fish", "Fish",
                "IgE cross-reactivity via parvalbumin across fish species",
                60, "HIGH",
                "Parvalbumin, the major fish allergen, is highly conserved across species. Individuals allergic to one fish species have a 50–80% likelihood of reacting to other species."),
            new("Shellfish", "Shellfish",
                "IgE cross-reactivity via tropomyosin across crustacean species",
                70, "HIGH",
                "Tropomyosin is highly conserved across crustacean shellfish species (shrimp, crab, lobster). Cross-reactivity between crustacean species is very common.")
        };

        // ── Endpoints ─────────────────────────────────────────────────────────

        /// <summary>
        /// Search ingredients by name. Returns up to 20 results.
        /// </summary>
        [HttpGet("search")]
        public async Task<ActionResult<List<IngredientSearchResult>>> Search(
            [FromQuery] string? q,
            [FromQuery] int limit = 10,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
                return Ok(new List<IngredientSearchResult>());

            limit = Math.Clamp(limit, 1, 20);
            var term = q.Trim().ToLowerInvariant();

            var results = await _db.GlobalIngredients
                .AsNoTracking()
                .Where(i => i.IsActive &&
                            (i.NormalizedName.Contains(term) ||
                             (i.Aliases != null && i.Aliases.ToLower().Contains(term))))
                .OrderByDescending(i => i.NormalizedName == term)          // exact match first
                .ThenByDescending(i => i.NormalizedName.StartsWith(term))  // prefix next
                .ThenBy(i => i.Name)
                .Take(limit)
                .Select(i => new IngredientSearchResult
                {
                    Id = i.Id,
                    Name = i.Name,
                    Slug = i.NormalizedName.Replace(" ", "-"),
                    NickelClassification = i.NickelClassification.ToString(),
                    NutritionalCategory = i.NutritionalCategory,
                    IsVerified = i.IsVerified
                })
                .ToListAsync(ct);

            return Ok(results);
        }

        /// <summary>
        /// Get full ingredient detail by slug.
        /// Slug is the normalised name with spaces replaced by hyphens (e.g. "whole-grain-oats").
        /// </summary>
        [HttpGet("{slug}")]
        public async Task<ActionResult<IngredientDetailResult>> GetBySlug(
            string slug,
            CancellationToken ct = default)
        {
            var normalizedName = slug.Trim().ToLowerInvariant().Replace("-", " ");

            var ingredient = await _db.GlobalIngredients
                .AsNoTracking()
                .Include(i => i.Allergens)
                .FirstOrDefaultAsync(
                    i => i.IsActive && i.NormalizedName == normalizedName, ct);

            if (ingredient == null)
                return NotFound(new { error = $"Ingredient '{slug}' not found." });

            var allergenTypes = ingredient.Allergens
                .Select(a => a.AllergenType)
                .Distinct()
                .OrderBy(a => a)
                .ToList();

            var crossReactivity = BuildCrossReactivity(allergenTypes);

            var result = new IngredientDetailResult
            {
                Id                        = ingredient.Id,
                Name                      = ingredient.Name,
                Slug                      = ingredient.NormalizedName.Replace(" ", "-"),
                NutritionalCategory       = ingredient.NutritionalCategory,
                DietaryFlags              = ParseFlags(ingredient.DietaryFlags),
                IsVerified                = ingredient.IsVerified,
                NickelClassification      = ingredient.NickelClassification.ToString(),
                NickelSummary             = BuildNickelSummary(ingredient),
                NickelMicrogramsPerServing = ingredient.NickelMicrogramsPerServing,
                NickelServingSizeGrams    = ingredient.NickelServingSizeGrams,
                NickelConfidenceLevel     = ingredient.NickelConfidenceLevel.ToString(),
                NickelDataPointCount      = ingredient.NickelDataPointCount,
                NickelHighVariability     = ingredient.NickelHighVariability,
                NickelPreparationNote     = ingredient.NickelPreparationNote,
                AllergenTypes             = allergenTypes,
                CrossReactivity           = crossReactivity,
                DataSourceCitation        =
                    "Nickel data sourced from Rebelytics R&D Inc., Nickel in Foods Summary Tables v9.1.1, " +
                    "September 2025. Compiled from 257 international food composition studies, " +
                    "20,575 individual measurements."
            };

            return Ok(result);
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static List<IngredientCrossReactivityEntry> BuildCrossReactivity(
            IEnumerable<string> allergenTypes)
        {
            var set = new HashSet<string>(allergenTypes, StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<IngredientCrossReactivityEntry>();

            foreach (var pair in CrossReactivityPairs)
            {
                if (!set.Contains(pair.AllergenA)) continue;

                // Deduplicate by related allergen name
                if (!seen.Add(pair.AllergenB)) continue;

                entries.Add(new IngredientCrossReactivityEntry
                {
                    SourceAllergen                = pair.AllergenA,
                    RelatedAllergen               = pair.AllergenB,
                    Mechanism                     = pair.Mechanism,
                    CoSensitisationLikelihoodPercent = pair.CoSensitisationLikelihood,
                    ClinicalSignificance          = pair.Significance,
                    ClinicalNote                  = pair.Note
                });
            }

            return entries;
        }

        private static string BuildNickelSummary(GlobalIngredient ingredient)
        {
            var mcg    = ingredient.NickelMicrogramsPerServing;
            var grams  = ingredient.NickelServingSizeGrams;
            var amtStr = mcg.HasValue
                ? $" (approximately {mcg:F1} μg per {grams ?? 100} g serving)"
                : string.Empty;

            return ingredient.NickelClassification switch
            {
                NickelClassification.Low =>
                    $"{ingredient.Name} contains a low level of nickel{amtStr}, meaning less than " +
                    "10 μg per serving. It is generally considered safe to include in a low-nickel diet " +
                    "when consumed in normal portions. People managing nickel contact allergy or systemic " +
                    "nickel allergy syndrome (SNAS) can typically eat low-nickel foods without triggering " +
                    "symptoms, though individual tolerance varies.",

                NickelClassification.Moderate =>
                    $"{ingredient.Name} contains a moderate level of nickel{amtStr}, in the range of " +
                    "10–20 μg per serving. People managing nickel allergy should be mindful of portion " +
                    "sizes and cumulative daily intake. The clinical low-nickel diet threshold is typically " +
                    "150 μg/day — moderate foods can be included in smaller amounts as part of a managed " +
                    "dietary plan developed with a registered dietitian.",

                NickelClassification.High =>
                    $"{ingredient.Name} is high in nickel{amtStr}, exceeding 20 μg per serving. " +
                    "This ingredient is commonly restricted or eliminated on a low-nickel diet. " +
                    "Individuals with nickel contact dermatitis or SNAS are typically advised to limit " +
                    "or avoid high-nickel foods, particularly during flare management. Always consult " +
                    "a registered dietitian before making significant dietary changes.",

                _ =>
                    $"No nickel content data is currently available for {ingredient.Name} in the " +
                    "Rebelytics v9.1.1 database. The absence of data does not indicate this ingredient " +
                    "is safe or unsafe for people managing nickel allergy. If nickel sensitivity is a " +
                    "concern, consult a registered dietitian for personalised guidance."
            };
        }

        private static List<string> ParseFlags(string? flags) =>
            string.IsNullOrWhiteSpace(flags)
                ? new List<string>()
                : flags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .ToList();
    }
}
