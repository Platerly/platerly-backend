using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Nutrition
{
    /// <summary>
    /// Identifies nutrient deficiency risks from a multi-allergen avoidance profile
    /// and returns compensating food recommendations validated for allergen safety
    /// and, optionally, low nickel content.
    ///
    /// Two embedded knowledge bases:
    ///   1. AllergenNutrientRisk  — maps each allergen to the nutrients it risks
    ///      (with base severity and clinical rationale).
    ///   2. NutrientFoodSources   — maps each nutrient to candidate compensating foods,
    ///      each annotated with which allergen groups they belong to so they can be
    ///      filtered out for allergic patients.
    ///
    /// DB enrichment (single bulk query per analysis):
    ///   - GlobalIngredients: nickel classification, nutritional category, dietary flags
    ///   - NutritionCaches:   USDA macros per 100 g (best-effort; null if unavailable)
    ///   - GlobalIngredientAllergens: belt-and-suspenders allergen safety confirmation
    ///
    /// Severity escalation:
    ///   Base severity from allergen-nutrient map; escalates to HIGH when two allergens
    ///   both restrict the same nutrient, and CRITICAL when three or more do.
    /// </summary>
    public class NutritionGapService : INutritionGapService
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<NutritionGapService> _logger;

        public NutritionGapService(PlaterlyDbContext db, ILogger<NutritionGapService> logger)
        {
            _db = db;
            _logger = logger;
        }

        // ── Knowledge base 1: Allergen → Nutrient risks ────────────────────────
        // Source: Dietitians of Canada, EAACI food allergy management guidelines,
        // Groetch M et al. "Nutrition and Dietary Management of Food Allergy" 2019.

        private record NutrientRisk(
            string Nutrient,
            int BaseSeverity,     // 1=LOW 2=MODERATE 3=HIGH
            string Rationale,
            string RdaReference);

        private static readonly Dictionary<string, NutrientRisk[]> AllergenNutrientRisks =
            new(StringComparer.OrdinalIgnoreCase)
        {
            ["Milk"] = new[]
            {
                new NutrientRisk("Calcium",   3, "Dairy provides ~70% of dietary calcium in Western diets. Milk allergy eliminates the most bioavailable calcium source.", "1000 mg/day (adults 19–50); 1300 mg/day (teens)"),
                new NutrientRisk("Vitamin D",  2, "Fortified milk is a primary Vitamin D source. Avoidance significantly reduces dietary intake.", "600 IU/day (adults 19–70); 800 IU/day (70+)"),
                new NutrientRisk("Vitamin B12",2, "Dairy contributes ~25% of daily B12 in typical diets. Higher risk when combined with egg avoidance.", "2.4 μg/day (adults)"),
                new NutrientRisk("Riboflavin", 2, "Milk is the largest single dietary riboflavin (B2) source, contributing ~35% of intake in dairy consumers.", "1.1–1.3 mg/day (adults)"),
                new NutrientRisk("Iodine",     2, "Dairy is the leading iodine source in many countries. Avoidance risks iodine deficiency, especially in pregnancy.", "150 μg/day; 220 μg/day (pregnancy)"),
            },
            ["Eggs"] = new[]
            {
                new NutrientRisk("Vitamin B12",2, "Eggs provide ~20% of daily B12. Combined with milk avoidance, the risk of deficiency escalates significantly.", "2.4 μg/day (adults)"),
                new NutrientRisk("Vitamin D",  2, "Egg yolk is one of the few non-fortified dietary Vitamin D sources. Avoidance reduces options significantly.", "600 IU/day (adults 19–70)"),
                new NutrientRisk("Choline",    2, "Eggs are the richest dietary choline source (~125 mg/egg). Choline is critical for liver function and fetal neurodevelopment.", "425–550 mg/day (adults)"),
                new NutrientRisk("Selenium",   1, "Eggs contribute ~25% of selenium intake in egg-eating populations.", "55 μg/day (adults)"),
                new NutrientRisk("Protein",    1, "Eggs are a complete protein source. Avoidance increases pressure on other protein sources.", "0.8 g/kg body weight/day"),
            },
            ["Fish"] = new[]
            {
                new NutrientRisk("Omega-3",    3, "Fatty fish (salmon, mackerel, sardines) are the only practical dietary source of long-chain EPA and DHA. Plant sources provide only ALA, which converts poorly.", "1.1–1.6 g/day ALA; 250–500 mg/day EPA+DHA (cardiovascular)"),
                new NutrientRisk("Vitamin D",  2, "Fatty fish is the highest natural Vitamin D food source. Fish avoidance removes this primary unfortified source.", "600 IU/day (adults 19–70)"),
                new NutrientRisk("Iodine",     2, "Seafood is the richest dietary iodine source outside of iodized salt and dairy.", "150 μg/day; 220 μg/day (pregnancy)"),
                new NutrientRisk("Selenium",   2, "Fish is a leading selenium source, providing ~45% of intake in fish-eating populations.", "55 μg/day (adults)"),
                new NutrientRisk("Protein",    1, "Fish is a high-quality complete protein. Avoidance increases reliance on land-animal and plant proteins.", "0.8 g/kg body weight/day"),
            },
            ["Shellfish"] = new[]
            {
                new NutrientRisk("Zinc",       2, "Oysters and shellfish are the most concentrated dietary zinc source (~74 mg/100g for oysters). Avoidance meaningfully reduces zinc supply.", "8–11 mg/day (adults)"),
                new NutrientRisk("Iodine",     2, "Shellfish, especially shrimp and oysters, are high-iodine foods. Avoidance alongside fish allergy creates significant iodine risk.", "150 μg/day (adults)"),
                new NutrientRisk("Selenium",   1, "Shrimp and other shellfish are moderate selenium sources contributing to daily intake.", "55 μg/day (adults)"),
                new NutrientRisk("Omega-3",    1, "Shellfish contribute modest EPA/DHA. Fish allergy co-occurrence escalates this gap significantly.", "250–500 mg/day EPA+DHA"),
            },
            ["Gluten"] = new[]
            {
                new NutrientRisk("Iron",       2, "Wheat and gluten-containing grains are heavily fortified with iron in most countries. Gluten avoidance eliminates these fortified sources.", "8–18 mg/day (adults; higher for menstruating women)"),
                new NutrientRisk("Thiamine",   2, "Fortified wheat flour is the dominant thiamine (B1) source in Western diets. Gluten-free products are often not fortified equivalently.", "1.1–1.2 mg/day (adults)"),
                new NutrientRisk("Riboflavin", 1, "Enriched flour contributes riboflavin; less impactful than dairy avoidance but meaningful in aggregate.", "1.1–1.3 mg/day (adults)"),
                new NutrientRisk("Niacin",     1, "Enriched wheat flour contributes niacin (B3). Gluten-free grain alternatives vary in fortification status.", "14–16 mg/day (adults)"),
                new NutrientRisk("Folate",     2, "Folic acid fortification of wheat flour has dramatically reduced neural tube defects. Gluten-free diets lose this mandatory fortification source.", "400 μg DFE/day; 600 μg DFE/day (pregnancy)"),
                new NutrientRisk("Fiber",      2, "Whole wheat and barley are major dietary fiber sources. Gluten-free substitutes are often lower in fiber.", "25–38 g/day (adults)"),
            },
            ["Soy"] = new[]
            {
                new NutrientRisk("Protein",    2, "Soy is the highest-quality plant protein (complete amino acid profile). Avoidance significantly limits plant-based protein options.", "0.8 g/kg body weight/day"),
                new NutrientRisk("Iron",       1, "Edamame and tofu are significant plant-based iron sources, especially in plant-forward diets.", "8–18 mg/day (adults)"),
                new NutrientRisk("Magnesium",  1, "Soy foods contribute meaningfully to magnesium intake in plant-based diets.", "310–420 mg/day (adults)"),
                new NutrientRisk("Calcium",    1, "Calcium-set tofu and fortified soy milk are important non-dairy calcium sources; avoidance limits options.", "1000 mg/day (adults 19–50)"),
            },
            ["Peanuts"] = new[]
            {
                new NutrientRisk("Vitamin E",  1, "Peanut butter is a significant vitamin E source in North American diets. Avoidance is low-impact if tree nuts are tolerated.", "15 mg/day (adults)"),
                new NutrientRisk("Niacin",     1, "Peanuts are among the richest plant sources of niacin (B3). Impact is modest unless combined with protein restriction.", "14–16 mg/day (adults)"),
                new NutrientRisk("Magnesium",  1, "Peanuts contribute to magnesium intake; impact is low if tree nuts and seeds are tolerated.", "310–420 mg/day (adults)"),
                new NutrientRisk("Healthy Fats",1,"Peanuts provide predominantly monounsaturated fats. Impact is modest when other fat sources are available.", "No RDA; prioritise MUFAs and PUFAs"),
            },
            ["Tree Nuts"] = new[]
            {
                new NutrientRisk("Vitamin E",  2, "Almonds are the richest dietary Vitamin E source (~26 mg/100g). Eliminating all tree nuts significantly impacts E intake.", "15 mg/day (adults)"),
                new NutrientRisk("Magnesium",  2, "Almonds, cashews, and Brazil nuts are top dietary magnesium sources. Avoidance of all tree nuts creates a meaningful gap.", "310–420 mg/day (adults)"),
                new NutrientRisk("Calcium",    1, "Almonds provide ~265 mg calcium per 100g. Less critical than dairy but relevant when multiple calcium sources are excluded.", "1000 mg/day (adults 19–50)"),
                new NutrientRisk("Selenium",   1, "Brazil nuts are the most concentrated selenium food (~1917 μg/100g). Avoidance is significant for selenium status if fish is also avoided.", "55 μg/day (adults)"),
                new NutrientRisk("Healthy Fats",1,"Tree nuts are a primary source of MUFAs, PUFAs, and ALA (walnuts). Avoidance increases reliance on oils and seeds.", "No RDA; prioritise MUFAs and PUFAs"),
            },
            ["Sesame"] = new[]
            {
                new NutrientRisk("Calcium",    1, "Sesame seeds and tahini contain ~975 mg calcium per 100g — higher than milk. A significant loss for plant-based calcium consumers.", "1000 mg/day (adults 19–50)"),
                new NutrientRisk("Iron",       1, "Sesame seeds are a rich plant-based iron source (~14 mg/100g). Relevant in plant-forward or multi-allergen diets.", "8–18 mg/day (adults)"),
            },
            ["Mustard"] = new[]
            {
                new NutrientRisk("Selenium",   1, "Mustard seeds contain modest selenium. Impact is negligible given the small quantities typically consumed.", "55 μg/day (adults)"),
            },
        };

        // ── Knowledge base 2: Nutrient → Compensating food sources ────────────
        // Each entry: (foodName, allergenGroups[], veganFriendly)
        // allergenGroups = which CFIA priority allergen categories this food triggers

        private record FoodSource(
            string Name,
            string[] AllergenGroups,
            bool VeganFriendly);

        private static readonly Dictionary<string, FoodSource[]> NutrientFoodSources =
            new(StringComparer.OrdinalIgnoreCase)
        {
            ["Calcium"] = new[]
            {
                new FoodSource("kale",              Array.Empty<string>(), true),
                new FoodSource("broccoli",          Array.Empty<string>(), true),
                new FoodSource("bok choy",          Array.Empty<string>(), true),
                new FoodSource("white beans",       Array.Empty<string>(), true),
                new FoodSource("chickpeas",         Array.Empty<string>(), true),
                new FoodSource("edamame",           new[]{"Soy"},          true),
                new FoodSource("tofu",              new[]{"Soy"},          true),
                new FoodSource("almonds",           new[]{"Tree Nuts"},    true),
                new FoodSource("sardines",          new[]{"Fish"},         false),
                new FoodSource("salmon",            new[]{"Fish"},         false),
            },
            ["Vitamin D"] = new[]
            {
                new FoodSource("salmon",            new[]{"Fish"},         false),
                new FoodSource("mackerel",          new[]{"Fish"},         false),
                new FoodSource("sardines",          new[]{"Fish"},         false),
                new FoodSource("tuna",              new[]{"Fish"},         false),
                new FoodSource("mushrooms",         Array.Empty<string>(), true),
                new FoodSource("beef liver",        Array.Empty<string>(), false),
                new FoodSource("pork",              Array.Empty<string>(), false),
                new FoodSource("chicken",           Array.Empty<string>(), false),
            },
            ["Vitamin B12"] = new[]
            {
                new FoodSource("beef",              Array.Empty<string>(), false),
                new FoodSource("chicken",           Array.Empty<string>(), false),
                new FoodSource("pork",              Array.Empty<string>(), false),
                new FoodSource("turkey",            Array.Empty<string>(), false),
                new FoodSource("salmon",            new[]{"Fish"},         false),
                new FoodSource("tuna",              new[]{"Fish"},         false),
                new FoodSource("sardines",          new[]{"Fish"},         false),
                new FoodSource("nutritional yeast", Array.Empty<string>(), true),
                new FoodSource("beef liver",        Array.Empty<string>(), false),
            },
            ["Iron"] = new[]
            {
                new FoodSource("beef",              Array.Empty<string>(), false),
                new FoodSource("chicken",           Array.Empty<string>(), false),
                new FoodSource("beef liver",        Array.Empty<string>(), false),
                new FoodSource("lentils",           Array.Empty<string>(), true),
                new FoodSource("kidney beans",      Array.Empty<string>(), true),
                new FoodSource("spinach",           Array.Empty<string>(), true),
                new FoodSource("pumpkin seeds",     Array.Empty<string>(), true),
                new FoodSource("quinoa",            Array.Empty<string>(), true),
                new FoodSource("dark chocolate",    Array.Empty<string>(), true),
                new FoodSource("tofu",              new[]{"Soy"},          true),
            },
            ["Omega-3"] = new[]
            {
                new FoodSource("flaxseed",          Array.Empty<string>(), true),
                new FoodSource("chia seeds",        Array.Empty<string>(), true),
                new FoodSource("hemp seeds",        Array.Empty<string>(), true),
                new FoodSource("flaxseed oil",      Array.Empty<string>(), true),
                new FoodSource("walnuts",           new[]{"Tree Nuts"},    true),
                new FoodSource("salmon",            new[]{"Fish"},         false),
                new FoodSource("mackerel",          new[]{"Fish"},         false),
                new FoodSource("sardines",          new[]{"Fish"},         false),
            },
            ["Vitamin E"] = new[]
            {
                new FoodSource("sunflower seeds",   Array.Empty<string>(), true),
                new FoodSource("avocado",           Array.Empty<string>(), true),
                new FoodSource("spinach",           Array.Empty<string>(), true),
                new FoodSource("sweet potato",      Array.Empty<string>(), true),
                new FoodSource("olive oil",         Array.Empty<string>(), true),
                new FoodSource("pumpkin seeds",     Array.Empty<string>(), true),
                new FoodSource("almonds",           new[]{"Tree Nuts"},    true),
                new FoodSource("hazelnuts",         new[]{"Tree Nuts"},    true),
            },
            ["Magnesium"] = new[]
            {
                new FoodSource("pumpkin seeds",     Array.Empty<string>(), true),
                new FoodSource("chia seeds",        Array.Empty<string>(), true),
                new FoodSource("spinach",           Array.Empty<string>(), true),
                new FoodSource("black beans",       Array.Empty<string>(), true),
                new FoodSource("quinoa",            Array.Empty<string>(), true),
                new FoodSource("dark chocolate",    Array.Empty<string>(), true),
                new FoodSource("avocado",           Array.Empty<string>(), true),
                new FoodSource("hemp seeds",        Array.Empty<string>(), true),
                new FoodSource("cashews",           new[]{"Tree Nuts"},    true),
                new FoodSource("almonds",           new[]{"Tree Nuts"},    true),
            },
            ["Zinc"] = new[]
            {
                new FoodSource("beef",              Array.Empty<string>(), false),
                new FoodSource("pork",              Array.Empty<string>(), false),
                new FoodSource("chicken",           Array.Empty<string>(), false),
                new FoodSource("turkey",            Array.Empty<string>(), false),
                new FoodSource("pumpkin seeds",     Array.Empty<string>(), true),
                new FoodSource("hemp seeds",        Array.Empty<string>(), true),
                new FoodSource("chickpeas",         Array.Empty<string>(), true),
                new FoodSource("lentils",           Array.Empty<string>(), true),
                new FoodSource("cashews",           new[]{"Tree Nuts"},    true),
            },
            ["Selenium"] = new[]
            {
                new FoodSource("chicken",           Array.Empty<string>(), false),
                new FoodSource("turkey",            Array.Empty<string>(), false),
                new FoodSource("beef",              Array.Empty<string>(), false),
                new FoodSource("pork",              Array.Empty<string>(), false),
                new FoodSource("mushrooms",         Array.Empty<string>(), true),
                new FoodSource("sunflower seeds",   Array.Empty<string>(), true),
                new FoodSource("tuna",              new[]{"Fish"},         false),
                new FoodSource("sardines",          new[]{"Fish"},         false),
                new FoodSource("brazil nuts",       new[]{"Tree Nuts"},    true),
            },
            ["Choline"] = new[]
            {
                new FoodSource("beef",              Array.Empty<string>(), false),
                new FoodSource("chicken",           Array.Empty<string>(), false),
                new FoodSource("beef liver",        Array.Empty<string>(), false),
                new FoodSource("pork",              Array.Empty<string>(), false),
                new FoodSource("mushrooms",         Array.Empty<string>(), true),
                new FoodSource("quinoa",            Array.Empty<string>(), true),
                new FoodSource("broccoli",          Array.Empty<string>(), true),
                new FoodSource("edamame",           new[]{"Soy"},          true),
                new FoodSource("salmon",            new[]{"Fish"},         false),
            },
            ["Iodine"] = new[]
            {
                new FoodSource("seaweed",           Array.Empty<string>(), true),
                new FoodSource("kelp",              Array.Empty<string>(), true),
                new FoodSource("cod",               new[]{"Fish"},         false),
                new FoodSource("tuna",              new[]{"Fish"},         false),
                new FoodSource("turkey",            Array.Empty<string>(), false),
                new FoodSource("chicken",           Array.Empty<string>(), false),
                new FoodSource("beef",              Array.Empty<string>(), false),
            },
            ["Riboflavin"] = new[]
            {
                new FoodSource("beef",              Array.Empty<string>(), false),
                new FoodSource("pork",              Array.Empty<string>(), false),
                new FoodSource("chicken",           Array.Empty<string>(), false),
                new FoodSource("turkey",            Array.Empty<string>(), false),
                new FoodSource("mushrooms",         Array.Empty<string>(), true),
                new FoodSource("quinoa",            Array.Empty<string>(), true),
                new FoodSource("spinach",           Array.Empty<string>(), true),
                new FoodSource("almonds",           new[]{"Tree Nuts"},    true),
            },
            ["Niacin"] = new[]
            {
                new FoodSource("chicken",           Array.Empty<string>(), false),
                new FoodSource("turkey",            Array.Empty<string>(), false),
                new FoodSource("beef",              Array.Empty<string>(), false),
                new FoodSource("pork",              Array.Empty<string>(), false),
                new FoodSource("mushrooms",         Array.Empty<string>(), true),
                new FoodSource("brown rice",        Array.Empty<string>(), true),
                new FoodSource("quinoa",            Array.Empty<string>(), true),
                new FoodSource("avocado",           Array.Empty<string>(), true),
                new FoodSource("salmon",            new[]{"Fish"},         false),
            },
            ["Folate"] = new[]
            {
                new FoodSource("lentils",           Array.Empty<string>(), true),
                new FoodSource("asparagus",         Array.Empty<string>(), true),
                new FoodSource("avocado",           Array.Empty<string>(), true),
                new FoodSource("spinach",           Array.Empty<string>(), true),
                new FoodSource("broccoli",          Array.Empty<string>(), true),
                new FoodSource("kidney beans",      Array.Empty<string>(), true),
                new FoodSource("black beans",       Array.Empty<string>(), true),
                new FoodSource("beef liver",        Array.Empty<string>(), false),
            },
            ["Fiber"] = new[]
            {
                new FoodSource("lentils",           Array.Empty<string>(), true),
                new FoodSource("black beans",       Array.Empty<string>(), true),
                new FoodSource("chickpeas",         Array.Empty<string>(), true),
                new FoodSource("avocado",           Array.Empty<string>(), true),
                new FoodSource("chia seeds",        Array.Empty<string>(), true),
                new FoodSource("quinoa",            Array.Empty<string>(), true),
                new FoodSource("broccoli",          Array.Empty<string>(), true),
                new FoodSource("pear",              Array.Empty<string>(), true),
                new FoodSource("apple",             Array.Empty<string>(), true),
                new FoodSource("sweet potato",      Array.Empty<string>(), true),
            },
            ["Protein"] = new[]
            {
                new FoodSource("chicken",           Array.Empty<string>(), false),
                new FoodSource("beef",              Array.Empty<string>(), false),
                new FoodSource("pork",              Array.Empty<string>(), false),
                new FoodSource("turkey",            Array.Empty<string>(), false),
                new FoodSource("lentils",           Array.Empty<string>(), true),
                new FoodSource("chickpeas",         Array.Empty<string>(), true),
                new FoodSource("quinoa",            Array.Empty<string>(), true),
                new FoodSource("hemp seeds",        Array.Empty<string>(), true),
                new FoodSource("pumpkin seeds",     Array.Empty<string>(), true),
                new FoodSource("black beans",       Array.Empty<string>(), true),
                new FoodSource("edamame",           new[]{"Soy"},          true),
                new FoodSource("tofu",              new[]{"Soy"},          true),
                new FoodSource("salmon",            new[]{"Fish"},         false),
                new FoodSource("tuna",              new[]{"Fish"},         false),
            },
            ["Thiamine"] = new[]
            {
                new FoodSource("pork",              Array.Empty<string>(), false),
                new FoodSource("sunflower seeds",   Array.Empty<string>(), true),
                new FoodSource("black beans",       Array.Empty<string>(), true),
                new FoodSource("lentils",           Array.Empty<string>(), true),
                new FoodSource("asparagus",         Array.Empty<string>(), true),
                new FoodSource("brown rice",        Array.Empty<string>(), true),
                new FoodSource("quinoa",            Array.Empty<string>(), true),
                new FoodSource("nutritional yeast", Array.Empty<string>(), true),
            },
            ["Healthy Fats"] = new[]
            {
                new FoodSource("avocado",           Array.Empty<string>(), true),
                new FoodSource("olive oil",         Array.Empty<string>(), true),
                new FoodSource("flaxseed",          Array.Empty<string>(), true),
                new FoodSource("chia seeds",        Array.Empty<string>(), true),
                new FoodSource("hemp seeds",        Array.Empty<string>(), true),
                new FoodSource("sunflower seeds",   Array.Empty<string>(), true),
                new FoodSource("pumpkin seeds",     Array.Empty<string>(), true),
                new FoodSource("walnuts",           new[]{"Tree Nuts"},    true),
                new FoodSource("almonds",           new[]{"Tree Nuts"},    true),
            },
        };

        // ── Analysis ───────────────────────────────────────────────────────────

        public async Task<NutritionGapResult> AnalyseGapsAsync(
            NutritionGapRequest request,
            CancellationToken ct = default)
        {
            var allergens = request.AllergenProfile
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var focusNutrients = request.FocusNutrients?
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var allergenSet = new HashSet<string>(allergens, StringComparer.OrdinalIgnoreCase);

            // ── 1. Identify unmodelled allergens ─────────────────────────────────

            var unmodelled = allergens
                .Where(a => !AllergenNutrientRisks.ContainsKey(a))
                .ToList();

            // ── 2. Aggregate nutrient risks with severity escalation ──────────────

            // nutrientName → (accumulatedSeverity, triggeringAllergens[], rationale, rda)
            var gapAccumulator = new Dictionary<string, (int severity, List<string> allergens, string rationale, string rda)>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var allergen in allergens)
            {
                if (!AllergenNutrientRisks.TryGetValue(allergen, out var risks)) continue;

                foreach (var risk in risks)
                {
                    if (focusNutrients != null && !focusNutrients.Contains(risk.Nutrient))
                        continue;

                    if (gapAccumulator.TryGetValue(risk.Nutrient, out var existing))
                    {
                        // Escalate: second allergen → +1 severity tier; third → CRITICAL (4)
                        int newSeverity = existing.allergens.Count == 1
                            ? Math.Min(existing.severity + 1, 4)
                            : Math.Min(existing.severity + 1, 4);
                        existing.allergens.Add(allergen);
                        gapAccumulator[risk.Nutrient] = (newSeverity, existing.allergens, existing.rationale, existing.rda);
                    }
                    else
                    {
                        gapAccumulator[risk.Nutrient] = (
                            risk.BaseSeverity,
                            new List<string> { allergen },
                            risk.Rationale,
                            risk.RdaReference);
                    }
                }
            }

            if (gapAccumulator.Count == 0)
            {
                return new NutritionGapResult
                {
                    OverallRisk          = "NONE",
                    TotalGapsIdentified  = 0,
                    NickelValidated      = request.NickelSensitive,
                    UnmodelledAllergens  = unmodelled
                };
            }

            // ── 3. Collect all candidate compensating food names ─────────────────

            int maxFoods = Math.Clamp(request.MaxFoodsPerGap, 1, 20);

            // Per-nutrient candidate foods (allergen-filtered by static knowledge base)
            var nutrientCandidates = new Dictionary<string, List<FoodSource>>(StringComparer.OrdinalIgnoreCase);

            var allCandidateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (nutrient, _) in gapAccumulator)
            {
                if (!NutrientFoodSources.TryGetValue(nutrient, out var sources)) continue;

                var filtered = sources
                    .Where(s => !s.AllergenGroups.Any(ag => allergenSet.Contains(ag)))
                    .ToList();

                nutrientCandidates[nutrient] = filtered;
                foreach (var f in filtered)
                    allCandidateNames.Add(f.Name.ToLower());
            }

            // ── 4. Bulk DB lookup for all candidates ─────────────────────────────

            var candidateNormalisedNames = allCandidateNames.ToList();

            var dbIngredients = await _db.GlobalIngredients
                .AsNoTracking()
                .Include(gi => gi.Allergens)
                .Where(gi => gi.IsActive && candidateNormalisedNames.Contains(gi.NormalizedName))
                .ToListAsync(ct);

            // HMAC alias fallback for unmatched candidates
            var matchedNorms = new HashSet<string>(
                dbIngredients.Select(gi => gi.NormalizedName),
                StringComparer.OrdinalIgnoreCase);

            var unmatchedCandidates = candidateNormalisedNames
                .Where(n => !matchedNorms.Contains(n))
                .ToList();

            if (unmatchedCandidates.Any())
            {
                var aliasCandidates = await _db.GlobalIngredients
                    .AsNoTracking()
                    .Include(gi => gi.Allergens)
                    .Where(gi => gi.IsActive && gi.Aliases != null)
                    .ToListAsync(ct);

                foreach (var cand in aliasCandidates)
                {
                    if (cand.Aliases == null) continue;
                    var aliases = cand.Aliases
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(a => a.ToLower());
                    foreach (var alias in aliases)
                    {
                        if (unmatchedCandidates.Contains(alias) && !matchedNorms.Contains(alias))
                        {
                            dbIngredients.Add(cand);
                            matchedNorms.Add(alias);
                        }
                    }
                }
            }

            var dbLookup = dbIngredients.ToDictionary(
                gi => gi.NormalizedName, StringComparer.OrdinalIgnoreCase);

            // ── 5. Bulk fetch NutritionCache for matched ingredients ──────────────

            var matchedIds = dbIngredients.Select(gi => gi.Id).ToList();
            var nutritionCaches = await _db.NutritionCaches
                .AsNoTracking()
                .Where(nc => matchedIds.Contains(nc.GlobalIngredientId))
                .ToListAsync(ct);

            var nutritionLookup = nutritionCaches.ToDictionary(nc => nc.GlobalIngredientId);

            // ── 6. Assemble nutrient gap entries ─────────────────────────────────

            var gapEntries = new List<NutrientGapEntry>();

            foreach (var (nutrient, (severity, trigAllergens, rationale, rda)) in gapAccumulator
                .OrderByDescending(kv => kv.Value.severity))
            {
                var candidates = nutrientCandidates.TryGetValue(nutrient, out var cands)
                    ? cands
                    : new List<FoodSource>();

                int excludedByAllergen = 0;
                if (NutrientFoodSources.TryGetValue(nutrient, out var allSources))
                    excludedByAllergen = allSources.Count(s =>
                        s.AllergenGroups.Any(ag => allergenSet.Contains(ag)));

                var foodEntries = new List<CompensatingFoodEntry>();
                int nickelExcluded = 0;

                foreach (var candidate in candidates)
                {
                    var normName = candidate.Name.ToLower();
                    dbLookup.TryGetValue(normName, out var gi);

                    // Belt-and-suspenders DB allergen check
                    bool dbAllergenSafe = gi == null || !gi.Allergens.Any(a =>
                        allergenSet.Contains(a.AllergenType) && !a.MayContain && !a.ProcessingAid);

                    if (!dbAllergenSafe) continue;

                    // Nickel filter
                    bool nickelDataMissing = gi == null || gi.NickelClassification == NickelClassification.Unknown;
                    bool isLowNickel       = gi != null && gi.NickelClassification == NickelClassification.Low;

                    if (request.NickelSensitive && gi != null &&
                        gi.NickelClassification != NickelClassification.Unknown &&
                        gi.NickelClassification != NickelClassification.Low)
                    {
                        nickelExcluded++;
                        continue;
                    }

                    // Build macros if nutrition cache available
                    FoodMacros? macros = null;
                    if (gi != null && nutritionLookup.TryGetValue(gi.Id, out var nc))
                    {
                        macros = new FoodMacros
                        {
                            CaloriesPer100g = nc.CaloriesPer100g,
                            ProteinPer100g  = nc.ProteinPer100g,
                            CarbsPer100g    = nc.CarbsPer100g,
                            FatPer100g      = nc.FatPer100g,
                            FiberPer100g    = nc.FiberPer100g
                        };
                    }

                    var dietaryFlags = gi?.DietaryFlags?
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList() ?? new List<string>();

                    foodEntries.Add(new CompensatingFoodEntry
                    {
                        Name                = gi?.Name ?? candidate.Name,
                        AllergenSafe        = true,
                        MatchedInDatabase   = gi != null,
                        NickelClassification = gi?.NickelClassification.ToString() ?? "Unknown",
                        NickelMcgPerServing = gi?.NickelMicrogramsPerServing,
                        NutritionalCategory = gi?.NutritionalCategory,
                        IsVerified          = gi?.IsVerified ?? false,
                        Macros              = macros,
                        DietaryFlags        = dietaryFlags,
                        NickelDataMissing   = nickelDataMissing
                    });

                    if (foodEntries.Count >= maxFoods) break;
                }

                gapEntries.Add(new NutrientGapEntry
                {
                    Nutrient           = nutrient,
                    RiskLevel          = SeverityLabel(severity),
                    TriggeringAllergens = trigAllergens,
                    Rationale          = rationale,
                    RdaReference       = rda,
                    CompensatingFoods  = foodEntries,
                    ExcludedFoodCount  = excludedByAllergen,
                    NickelExcludedCount = nickelExcluded
                });
            }

            // ── 7. Overall risk and priority nutrients ────────────────────────────

            int maxSev = gapAccumulator.Values.Max(v => v.severity);
            string overallRisk = maxSev switch
            {
                4 => "CRITICAL",
                3 => "HIGH",
                2 => "MODERATE",
                1 => "LOW",
                _ => "NONE"
            };

            var priorityNutrients = gapEntries
                .Where(g => g.RiskLevel is "CRITICAL" or "HIGH")
                .Select(g => g.Nutrient)
                .ToList();

            if (!priorityNutrients.Any())
                priorityNutrients = gapEntries
                    .Where(g => g.RiskLevel == "MODERATE")
                    .Select(g => g.Nutrient)
                    .Take(3)
                    .ToList();

            return new NutritionGapResult
            {
                NutrientGaps        = gapEntries,
                OverallRisk         = overallRisk,
                PriorityNutrients   = priorityNutrients,
                TotalGapsIdentified = gapEntries.Count,
                NickelValidated     = request.NickelSensitive,
                UnmodelledAllergens = unmodelled
            };
        }

        private static string SeverityLabel(int severity) => severity switch
        {
            4 => "CRITICAL",
            3 => "HIGH",
            2 => "MODERATE",
            _ => "LOW"
        };
    }
}
