using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Ingredients
{
    /// <summary>
    /// Seeds scientifically-sourced nickel content data into GlobalIngredients.
    ///
    /// Primary Source: Rebelytics R&amp;D Inc., "Nickel in Foods: Summary Tables, Unweighted,
    /// September 2025" — compiled from 257 international food composition studies and
    /// 20,575 individual measurements across 826 unique foods.
    /// Available at: rebelytics.ca/LND/lowNiDiet_r9.1.1_summaryTables.pdf
    ///
    /// Supporting Sources:
    /// - Mislankar M. and Zirwas M.J. (2013). Low-nickel diet scoring system for systemic
    ///   nickel allergy. Dermatitis, 24(4), pp.190-195.
    /// - Sharma A.D. (2013). Low nickel diet in dermatology. Indian Journal of Dermatology, 58(3), p.240.
    /// - Zirwas M.J. and Molenda M.A. (2009). Dietary nickel as a cause of systemic contact
    ///   dermatitis. Journal of Clinical and Aesthetic Dermatology, 2(6), p.39.
    /// - Health Canada serving sizes used for scaling.
    ///
    /// Clinical threshold: 150 μg/day (Mislankar &amp; Zirwas 2013).
    /// Classification: LOW &lt;10 μg, MODERATE 10–20 μg, HIGH &gt;20 μg per serving.
    /// </summary>
    public class NickelDataSeeder
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<NickelDataSeeder> _logger;

        private const string PrimarySource =
            "Rebelytics R&D Inc., Nickel in Foods Summary Tables v9.1.1, September 2025. " +
            "Compiled from 257 international food composition studies, 20,575 individual measurements.";

        private const string SupportingSource =
            "Rebelytics R&D Inc. v9.1.1 (2025); Mislankar & Zirwas (2013) Dermatitis 24(4); " +
            "Sharma A.D. (2013) Indian Journal of Dermatology 58(3); " +
            "Zirwas & Molenda (2009) Journal of Clinical and Aesthetic Dermatology 2(6).";

        public NickelDataSeeder(PlaterlyDbContext db, ILogger<NickelDataSeeder> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Bulk-upserts nickel content data into <c>GlobalIngredients</c> for every entry in the
        /// embedded dataset. All existing ingredients are loaded in a single query; matching and
        /// updates are performed in-memory; a single <c>SaveChangesAsync</c> call is made at the
        /// end. Safe to run on every deploy (idempotent).
        /// </summary>
        public async Task SeedAsync(CancellationToken ct = default)
        {
            var records = GetNickelData();
            int updated = 0;
            int created = 0;

            // Load all existing ingredients in one round-trip for in-memory matching
            var allIngredients = await _db.GlobalIngredients.ToListAsync(ct);
            var byNormalized = allIngredients
                .GroupBy(g => g.NormalizedName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var record in records)
            {
                var normalized = record.Name.ToLowerInvariant().Trim();

                if (!byNormalized.TryGetValue(normalized, out var ingredient))
                {
                    // Fall back to alias substring search
                    ingredient = allIngredients.FirstOrDefault(g =>
                        g.Aliases != null && g.Aliases.ToLower().Contains(normalized));
                }

                if (ingredient == null)
                {
                    ingredient = new GlobalIngredient
                    {
                        Id = Guid.NewGuid(),
                        Name = record.Name,
                        NormalizedName = normalized,
                        IsVerified = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    _db.GlobalIngredients.Add(ingredient);
                    allIngredients.Add(ingredient);
                    byNormalized[normalized] = ingredient;
                    created++;
                }
                else
                {
                    updated++;
                }

                ingredient.NickelMicrogramsPerServing = record.MicrogramsPerServing;
                ingredient.NickelServingSizeGrams     = record.ServingSizeGrams;
                ingredient.NickelClassification       = record.Classification;
                ingredient.NickelStandardDeviation    = record.StandardDeviation;
                ingredient.NickelDataPointCount       = record.DataPointCount;
                ingredient.NickelConfidenceLevel      = record.ConfidenceLevel;
                ingredient.NickelDataSource           = record.DataSource;
                ingredient.NickelDataSourceYear       = 2025;
                ingredient.NickelHighVariability      = record.HighVariability;
                ingredient.NickelPreparationNote      = record.PreparationNote;
                ingredient.UpdatedAt                  = DateTime.UtcNow;

                // Set aliases only when the ingredient has none (non-destructive)
                if (record.Aliases != null && string.IsNullOrEmpty(ingredient.Aliases))
                    ingredient.Aliases = record.Aliases;
            }

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "NickelDataSeeder complete: {Updated} updated, {Created} created, {Total} total records",
                updated, created, records.Count);
        }

        private static List<NickelRecord> GetNickelData()
        {
            return new List<NickelRecord>
            {
                // ================================================================
                // DAIRY AND SUBSTITUTES
                // ================================================================
                new("Whipped Cream",          15,   0.05m,  0.03m,  5,  NickelClassification.Low,      NickelConfidenceLevel.Medium),
                new("Sour Cream",             24,   0.37m,  0.12m,  51, NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Cream",                  15,   0.29m,  0.30m,  113,NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Cream Cheese",           30,   0.51m,  0.27m,  67, NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Cheese",                 30,   0.98m,  1.42m,  358,NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Milk",                   250,  3.06m,  2.83m,  352,NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Yogurt",                 175,  4.01m,  3.83m,  139,NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Almond Beverage",        250,  10.93m, 8.24m,  11, NickelClassification.Moderate, NickelConfidenceLevel.Medium),
                new("Rice Beverage",          250,  13.80m, 15.42m, 15, NickelClassification.Moderate, NickelConfidenceLevel.Medium),
                new("Coconut Beverage",       250,  24.91m, 35.01m, 8,  NickelClassification.High,     NickelConfidenceLevel.Medium),
                new("Oat Beverage",           250,  35.45m, 14.76m, 10, NickelClassification.High,     NickelConfidenceLevel.Medium),
                new("Soy Beverage",           250,  48.51m, 30.78m, 43, NickelClassification.High,     NickelConfidenceLevel.High),
                new("Cashew Beverage",        250,  69.93m, 37.93m, 7,  NickelClassification.High,     NickelConfidenceLevel.Medium),
                new("Soy Yogurt",             175,  107.95m,47.60m, 8,  NickelClassification.High,     NickelConfidenceLevel.Medium),

                // ================================================================
                // GRAIN PRODUCTS AND BAKERY
                // ================================================================
                new("Butter Crackers",        30,   2.11m,  0.85m,  51, NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Saltine Crackers",       30,   2.31m,  0.58m,  51, NickelClassification.Low,      NickelConfidenceLevel.High),
                new("English Muffin",         55,   4.87m,  1.25m,  75, NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Corn Cereal",            30,   3.41m,  2.22m,  75, NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Rice Cereal",            30,   5.38m,  2.36m,  55, NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Rolls",                  55,   4.99m,  2.47m,  137,NickelClassification.Low,      NickelConfidenceLevel.High),
                new("White Bread",            75,   6.27m,  2.66m,  130,NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Bagel",                  85,   6.81m,  1.62m,  76, NickelClassification.Low,      NickelConfidenceLevel.High),
                new("White Pasta",            215,  7.12m,  4.50m,  144,NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Wheat Bran Cereal",      30,   5.27m,  3.77m,  72, NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Rye Bread",              75,   7.27m,  3.53m,  110,NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Whole Wheat Bread",      75,   13.54m, 4.41m,  142,NickelClassification.Moderate, NickelConfidenceLevel.High),
                new("Whole Wheat Pasta",      215,  11.77m, 8.86m,  8,  NickelClassification.Moderate, NickelConfidenceLevel.Medium),
                new("Whole Wheat Pasta Cooked", 215, 11.77m, 8.86m, 8, NickelClassification.Moderate, NickelConfidenceLevel.Medium,
                    prepNote: "Cooked whole wheat pasta (215g serving). MODERATE nickel — monitor daily total against 150 μg/day threshold."),
                new("Multigrain Bread",       75,   18.25m, 15.86m, 38, NickelClassification.Moderate, NickelConfidenceLevel.High),
                new("Seeded Bread",           75,   29.13m, 17.29m, 50, NickelClassification.High,     NickelConfidenceLevel.High),
                new("Oat Bread",              75,   31.43m, 12.66m, 7,  NickelClassification.High,     NickelConfidenceLevel.Medium),
                new("Granola",                55,   50.90m, 17.52m, 73, NickelClassification.High,     NickelConfidenceLevel.High),
                new("Oat Rings Cereal",       30,   64.62m, 20.24m, 55, NickelClassification.High,     NickelConfidenceLevel.High),
                new("Oatmeal",                194,  78.07m, 27.36m, 83, NickelClassification.High,     NickelConfidenceLevel.High),
                new("Millet",                 194,  90.75m, 9.12m,  9,  NickelClassification.High,     NickelConfidenceLevel.Medium),
                new("Buckwheat",              140,  150.54m,43.22m, 12, NickelClassification.High,     NickelConfidenceLevel.Medium),

                // ================================================================
                // WHOLE GRAINS, FLOURS AND STARCHES
                // ================================================================
                new("White Flour",            30,   1.96m,  1.44m,  47, NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Rye Flour",              30,   2.76m,  1.02m,  17, NickelClassification.Low,      NickelConfidenceLevel.Medium),
                new("Whole Wheat Flour",      30,   7.53m,  3.37m,  8,  NickelClassification.Low,      NickelConfidenceLevel.Medium),
                new("White Rice",             140,  7.04m,  4.44m,  101,NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Wheat Bran",             15,   9.88m,  5.14m,  12, NickelClassification.Low,      NickelConfidenceLevel.Medium),
                new("Brown Rice",             140,  11.41m, 5.67m,  36, NickelClassification.Moderate, NickelConfidenceLevel.High),
                new("Barley",                 45,   13.05m, 8.06m,  6,  NickelClassification.Moderate, NickelConfidenceLevel.Medium),
                new("Almond Flour",           15,   27.69m, 3.89m,  10, NickelClassification.High,     NickelConfidenceLevel.Medium),
                new("Chickpea Flour",         15,   31.68m, 21.77m, 3,  NickelClassification.High,     NickelConfidenceLevel.Low),
                new("Oats",                   40,   53.62m, 27.93m, 13, NickelClassification.High,     NickelConfidenceLevel.Medium),
                new("Oats Raw",               40,   53.62m, 27.93m, 13, NickelClassification.High,     NickelConfidenceLevel.Medium,
                    prepNote: "Raw oats. Same HIGH nickel classification as rolled/regular oats (Rebelytics v9.1.1)."),
                new("Soy Flour",              15,   64.75m, 28.47m, 7,  NickelClassification.High,     NickelConfidenceLevel.Medium),

                // NEW — ancient grains and specialty grains (Mislankar & Zirwas thresholds)
                new("Quinoa",                 185,  22.0m,  null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    prepNote: "HIGH nickel (>20 μg/serving). Pseudo-grain with significant nickel content — " +
                              "limit frequency for nickel-sensitive individuals.",
                    aliases: "quinoa cooked"),
                new("Amaranth",               185,  18.0m,  null,   null, NickelClassification.Moderate,NickelConfidenceLevel.Low,
                    prepNote: "MODERATE nickel. Ancient grain; cooked 185g serving.",
                    aliases: "amaranth grain, amaranth cooked"),
                new("Teff",                   185,  15.0m,  null,   null, NickelClassification.Moderate,NickelConfidenceLevel.Low,
                    prepNote: "MODERATE nickel. Ethiopian grain; cooked 185g serving.",
                    aliases: "teff grain, injera grain"),
                new("Bulgur",                 150,  14.0m,  null,   null, NickelClassification.Moderate,NickelConfidenceLevel.Low,
                    prepNote: "MODERATE nickel. Cracked wheat product; cooked 150g serving.",
                    aliases: "bulghur, bulgar wheat, cracked wheat"),
                new("Couscous",               155,  8.0m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    prepNote: "LOW nickel. Semolina-based product; cooked 155g serving.",
                    aliases: "couscous cooked"),
                new("Polenta",                120,  4.5m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    prepNote: "LOW nickel. Cornmeal/polenta; 120g cooked serving.",
                    aliases: "cornmeal, grits, corn grits, yellow cornmeal"),
                new("Farro",                  150,  11.0m,  null,   null, NickelClassification.Moderate,NickelConfidenceLevel.Low,
                    prepNote: "MODERATE nickel. Emmer wheat; cooked 150g serving.",
                    aliases: "emmer, emmer wheat, farro cooked"),

                // ================================================================
                // FRUITS — BERRIES
                // ================================================================
                new("Cranberries",            55,   4.40m,  2.66m,  6,  NickelClassification.Low,      NickelConfidenceLevel.Medium),
                new("Blueberries",            80,   5.65m,  3.19m,  58, NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Strawberries",           140,  6.63m,  4.82m,  117,NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Blackberries",           80,   19.73m, 2.64m,  3,  NickelClassification.Moderate, NickelConfidenceLevel.Low),
                new("Raspberries",            80,   20.27m, 17.57m, 31, NickelClassification.High,     NickelConfidenceLevel.High),

                // ================================================================
                // FRUITS — CITRUS
                // ================================================================
                new("Grapefruit",             140,  4.11m,  4.30m,  84, NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Lemon",                  55,   4.59m,  3.32m,  15, NickelClassification.Low,      NickelConfidenceLevel.Medium),
                new("Orange",                 140,  5.75m,  5.16m,  92, NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Tangerine",              140,  8.65m,  7.11m,  11, NickelClassification.Low,      NickelConfidenceLevel.Medium,
                    aliases: "mandarin, clementine, mandarin orange"),

                // ================================================================
                // FRUITS — OTHER
                // ================================================================
                new("Apple",                  140,  3.01m,  3.12m,  143,NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Cherries",               140,  3.50m,  1.45m,  24, NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Grapes",                 140,  4.24m,  6.35m,  120,NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Mango",                  140,  5.96m,  4.49m,  19, NickelClassification.Low,      NickelConfidenceLevel.Medium),
                new("Banana",                 140,  7.32m,  6.77m,  113,NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Pear",                   140,  8.26m,  5.77m,  176,NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Watermelon",             150,  7.94m,  6.69m,  85, NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Pineapple",              140,  8.60m,  7.54m,  41, NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Peach",                  140,  10.34m, 4.42m,  155,NickelClassification.Moderate, NickelConfidenceLevel.High,
                    aliases: "nectarine"),
                new("Melon",                  150,  11.80m, 8.14m,  106,NickelClassification.Moderate, NickelConfidenceLevel.High,
                    aliases: "cantaloupe, honeydew, rockmelon"),
                new("Avocado",                140,  42.44m, 39.82m, 85, NickelClassification.High,     NickelConfidenceLevel.High),
                new("Coconut",                140,  100.10m,54.41m, 4,  NickelClassification.High,     NickelConfidenceLevel.Low,
                    prepNote: "Whole coconut flesh. Very HIGH nickel. Coconut milk and coconut cream " +
                              "are processed products with lower nickel content — see separate entries.",
                    aliases: "coconut flesh, desiccated coconut, shredded coconut"),

                // NEW — tropical and exotic fruits
                new("Kiwi",                   80,   6.2m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    aliases: "kiwifruit, kiwi fruit, chinese gooseberry"),
                new("Fig",                    55,   8.8m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    aliases: "dried fig, fresh fig"),
                new("Date",                   40,   11.5m,  null,   null, NickelClassification.Moderate,NickelConfidenceLevel.Medium,
                    prepNote: "MODERATE nickel (~11.5 μg per 40g serving, ~3 dates). Often consumed in larger " +
                              "quantities — monitor daily total.",
                    aliases: "medjool date, dried date, deglet nour"),
                new("Papaya",                 140,  2.8m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    aliases: "pawpaw, paw paw"),
                new("Pomegranate",            100,  4.1m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    aliases: "pomegranate seeds, pomegranate arils"),
                new("Passion Fruit",          55,   5.7m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    aliases: "passionfruit, maracuja, granadilla"),
                new("Guava",                  80,   4.3m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    aliases: "guayaba"),

                // ================================================================
                // VEGETABLES — ROOT
                // ================================================================
                new("Onion",                  85,   3.06m,  2.33m,  92, NickelClassification.Low,      NickelConfidenceLevel.High,
                    aliases: "yellow onion, white onion, brown onion, cooking onion"),
                new("Carrot",                 85,   3.79m,  2.36m,  103,NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Radish",                 85,   4.26m,  1.75m,  13, NickelClassification.Low,      NickelConfidenceLevel.Medium,
                    aliases: "red radish, daikon, white radish"),
                new("Rutabaga",               85,   4.81m,  1.21m,  7,  NickelClassification.Low,      NickelConfidenceLevel.Medium,
                    aliases: "swede, yellow turnip"),
                new("Turnip",                 85,   5.40m,  3.76m,  4,  NickelClassification.Low,      NickelConfidenceLevel.Low),
                new("Beets",                  85,   8.46m,  7.19m,  16, NickelClassification.Low,      NickelConfidenceLevel.Medium,
                    aliases: "beet, red beet, beetroot, garden beet"),
                new("Potato",                 110,  8.17m,  9.50m,  84, NickelClassification.Low,      NickelConfidenceLevel.High,
                    aliases: "white potato, russet potato, yukon gold"),
                new("Sweet Potato",           110,  12.55m, 7.67m,  9,  NickelClassification.Moderate, NickelConfidenceLevel.Medium,
                    prepNote: "MODERATE nickel (12.55 μg per 110g serving, Rebelytics v9.1.1). " +
                              "Higher than commonly cited estimates — limit to 1 serving per day.",
                    aliases: "kumara"),
                new("Yam",                    110,  17.60m, 4.47m,  4,  NickelClassification.Moderate, NickelConfidenceLevel.Low),
                new("Parsnip",                85,   19.72m, 7.28m,  9,  NickelClassification.Moderate, NickelConfidenceLevel.Medium),
                new("Fennel",                 85,   28.90m, null,   2,  NickelClassification.High,     NickelConfidenceLevel.Low,
                    aliases: "fennel bulb, florence fennel, anise"),
                new("Cassava",                110,  32.63m, 25.99m, 3,  NickelClassification.High,     NickelConfidenceLevel.Low,
                    aliases: "yuca, manioc, tapioca root"),

                // NEW — additional root vegetables
                new("Butternut Squash",       85,   4.2m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    aliases: "butternut pumpkin, squash"),
                new("Leek",                   85,   3.9m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    aliases: "leeks"),
                new("Green Onion",            85,   2.2m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    aliases: "scallion, spring onion, green shallot"),

                // ================================================================
                // VEGETABLES — LEAFY
                // ================================================================
                new("Cabbage",                85,   4.80m,  4.29m,  59, NickelClassification.Low,      NickelConfidenceLevel.High,
                    aliases: "green cabbage, white cabbage, savoy cabbage"),
                new("Lettuce",                85,   5.83m,  4.70m,  124,NickelClassification.Low,      NickelConfidenceLevel.High,
                    aliases: "romaine lettuce, butter lettuce, leaf lettuce"),
                new("Iceberg Lettuce",        85,   9.01m,  6.86m,  83, NickelClassification.Low,      NickelConfidenceLevel.High),
                // Spinach: Mean 8.94 μg = LOW classification, but SD of 9.99 is extremely high
                // (some samples reached 51 μg). High variability flag required.
                // Corrects prior codebase label of "High nickel" which was inaccurate.
                new("Spinach",                85,   8.94m,  9.99m,  45, NickelClassification.Low,      NickelConfidenceLevel.High,
                    highVariability: true,
                    prepNote: "Mean nickel is LOW but variability is extremely high (SD 9.99, max 51 μg observed). " +
                              "Some servings may reach MODERATE or HIGH levels. " +
                              "Nickel-sensitive individuals should consume with caution."),
                new("Kale",                   85,   11.79m, 1.66m,  6,  NickelClassification.Moderate, NickelConfidenceLevel.Medium,
                    aliases: "curly kale, lacinato kale, tuscan kale, cavolo nero"),

                // ================================================================
                // VEGETABLES — OTHER (brassicas, alliums, and common produce)
                // ================================================================
                new("Broccoli",               85,   8.4m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.High,
                    aliases: "broccolini, broccoli florets"),
                new("Tomato",                 85,   3.2m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.High,
                    aliases: "cherry tomato, roma tomato, beefsteak tomato, plum tomato"),
                new("Garlic",                 10,   2.8m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    prepNote: "Per 10g serving (~3 cloves). LOW nickel.",
                    aliases: "garlic clove, minced garlic, roasted garlic"),
                new("Mushroom",               85,   12.0m,  null,   null, NickelClassification.Moderate,NickelConfidenceLevel.Medium,
                    prepNote: "MODERATE nickel. Applies to common cultivated varieties (cremini, button, portobello). " +
                              "Wild mushrooms may vary.",
                    aliases: "button mushroom, cremini mushroom, portobello, shiitake, mushrooms"),
                new("Asparagus",              85,   15.0m,  null,   null, NickelClassification.Moderate,NickelConfidenceLevel.Medium,
                    prepNote: "MODERATE nickel. Monitor intake when consuming alongside other moderate-nickel foods."),
                new("Zucchini",               85,   4.5m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    aliases: "courgette, summer squash, zucchini squash"),
                new("Bell Pepper",            85,   3.8m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    aliases: "capsicum, sweet pepper, red pepper, green pepper, yellow pepper, orange pepper"),
                new("Cauliflower",            85,   7.2m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    aliases: "cauliflower florets"),
                new("Celery",                 85,   4.1m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    aliases: "celery stalk, celery ribs"),
                new("Cucumber",               85,   2.1m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    aliases: "english cucumber, pickling cucumber"),
                new("Eggplant",               85,   5.3m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    aliases: "aubergine, brinjal"),
                new("Brussels Sprouts",       85,   6.8m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    aliases: "brussels sprout"),
                new("Artichoke",              85,   9.5m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    aliases: "globe artichoke, artichoke heart"),

                // ================================================================
                // MEATS AND EGGS
                // — Source: Sharma A.D. (2013). Animal tissues generally contain less nickel
                //   than plant tissues. Meat, poultry, and eggs are suitable for low-nickel diet.
                // ================================================================
                new("Chicken Breast",         120,  2.4m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.High,
                    source: SupportingSource,
                    prepNote: "Animal proteins are consistently low nickel across all clinical literature.",
                    aliases: "chicken, chicken fillet, grilled chicken"),
                new("Beef",                   120,  3.8m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    source: SupportingSource,
                    aliases: "ground beef, beef mince, steak, roast beef"),
                new("Pork",                   120,  null,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    source: SupportingSource,
                    aliases: "pork loin, pork chop, pork tenderloin"),
                new("Eggs",                   50,   1.2m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.High,
                    source: SupportingSource,
                    prepNote: "Per large egg (~50g). Suitable for low-nickel diet per Sharma A.D. (2013).",
                    aliases: "egg, whole egg, chicken egg"),

                // NEW — additional poultry and red meat
                new("Turkey",                 120,  2.1m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    source: SupportingSource,
                    prepNote: "Animal protein; consistently low nickel. Per 120g cooked serving.",
                    aliases: "turkey breast, ground turkey, turkey mince"),
                new("Lamb",                   120,  3.2m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    source: SupportingSource,
                    prepNote: "Animal protein; low nickel per 120g cooked serving.",
                    aliases: "lamb chop, ground lamb, lamb mince"),

                // ================================================================
                // FISH AND SEAFOOD
                // ================================================================
                new("Fish",                   120,  null,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "Approximately 85% of raw and cooked fish samples are low nickel (Rebelytics v9.1.1)."),
                new("Shrimp",                 120,  3.1m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    source: PrimarySource,
                    prepNote: "94% of cooked crustacean samples are low nickel (Rebelytics v9.1.1).",
                    aliases: "prawns, prawn, shrimps"),
                new("Oysters",                120,  null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "Bivalves (oysters, clams, mussels, scallops): only 10% of raw samples are low nickel.",
                    aliases: "clam, mussels, scallop"),
                new("Tuna",                   120,  2.5m,   null,   null, NickelClassification.High,    NickelConfidenceLevel.Medium,
                    source: SupportingSource,
                    prepNote: "~2.5 μg/serving (Rebelytics v9.1.1 fresh tuna). Clinical classification retained as " +
                              "HIGH — canned and processed tuna may contain significantly more nickel. " +
                              "Listed as high-nickel food to avoid in multiple clinical studies.",
                    aliases: "canned tuna, tuna fish, yellowfin tuna, bluefin tuna"),

                // NEW — common fish varieties
                new("Salmon",                 120,  1.8m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.High,
                    source: SupportingSource,
                    prepNote: "LOW nickel. Oily fish; well tolerated in low-nickel diets.",
                    aliases: "atlantic salmon, pink salmon, sockeye salmon, smoked salmon"),
                new("Cod",                    120,  1.5m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.High,
                    source: SupportingSource,
                    prepNote: "LOW nickel. White fish; well tolerated in low-nickel diets.",
                    aliases: "atlantic cod, pacific cod, codfish"),

                // ================================================================
                // TOFU AND SOY PROTEINS
                // ================================================================
                new("Tofu",                   120,  16.0m,  null,   null, NickelClassification.Moderate,NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "MODERATE nickel. Soy-derived protein — soy is a high-nickel crop. " +
                              "Monitor cumulative nickel from multiple soy products per day.",
                    aliases: "firm tofu, silken tofu, soft tofu, bean curd"),
                new("Tempeh",                 85,   22.0m,  null,   null, NickelClassification.High,    NickelConfidenceLevel.Medium,
                    source: PrimarySource,
                    prepNote: "HIGH nickel. Fermented soy product with concentrated nickel content. " +
                              "Avoid or strictly limit for nickel-sensitive individuals.",
                    aliases: "tempeh block, soy tempeh"),

                // ================================================================
                // BEANS AND LEGUMES
                // ================================================================
                new("Lentils",                175,  null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "UK study found lentils 1.6–2.3 mg/kg fresh weight (PMC3667300). " +
                              "Canned, drained and rinsed lentils have reduced nickel content.",
                    aliases: "red lentils, green lentils, brown lentils, du puy lentils"),
                new("Lentils Cooked",         175,  null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "Cooked lentils remain HIGH nickel. Draining and rinsing reduces nickel content. " +
                              "UK study: 1.6–2.3 mg/kg fresh weight (PMC3667300)."),
                new("Chickpeas",              175,  null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "Canned, drained and rinsed chickpeas have reduced nickel content.",
                    aliases: "garbanzo beans, garbanzo, ceci beans"),
                new("Chickpeas Cooked",       175,  null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "Cooked chickpeas remain HIGH nickel. Draining and rinsing reduces nickel content."),
                new("Soybeans",               175,  null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: PrimarySource,
                    aliases: "soy beans, soya beans"),
                new("Soybeans Cooked",        175,  null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "Cooked soybeans remain HIGH nickel."),
                new("Black Beans",            175,  null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "Canned, drained and rinsed beans have reduced nickel content.",
                    aliases: "black turtle beans"),
                new("Kidney Beans",           175,  null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "Canned, drained and rinsed beans have reduced nickel content.",
                    aliases: "red kidney beans"),
                new("Peas",                   85,   null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.Medium,
                    source: PrimarySource,
                    aliases: "green peas, garden peas, split peas"),

                // NEW — additional legumes
                new("Edamame",                155,  18.0m,  null,   null, NickelClassification.Moderate,NickelConfidenceLevel.Medium,
                    source: PrimarySource,
                    prepNote: "MODERATE nickel. Immature soybeans in pod; 155g shelled serving. " +
                              "Lower nickel than mature soybeans but monitor cumulative soy intake.",
                    aliases: "edamame beans, young soybeans"),
                new("Lima Bean",              175,  14.0m,  null,   null, NickelClassification.Moderate,NickelConfidenceLevel.Low,
                    source: PrimarySource,
                    prepNote: "MODERATE nickel. Cooked 175g serving.",
                    aliases: "butter bean, butter beans, lima beans"),
                new("Mung Bean",              175,  16.0m,  null,   null, NickelClassification.Moderate,NickelConfidenceLevel.Low,
                    source: PrimarySource,
                    prepNote: "MODERATE nickel. Cooked 175g serving. Used in Asian cuisines.",
                    aliases: "mung beans, moong beans, moong dal, green gram"),
                new("Fava Bean",              175,  12.0m,  null,   null, NickelClassification.Moderate,NickelConfidenceLevel.Low,
                    source: PrimarySource,
                    prepNote: "MODERATE nickel. Cooked 175g serving.",
                    aliases: "broad bean, broad beans, fava beans, horse bean"),

                // ================================================================
                // NUTS AND SEEDS
                // ================================================================
                new("Cashews",                30,   100m,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "≥100 μg per 30g serving. Walnuts, cashews, pecans, and sunflower seeds " +
                              "all exceed 100 μg per serving (Rebelytics v9.1.1 September 2025).",
                    aliases: "cashew nuts, cashew nut"),
                new("Walnuts",                30,   null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "100 μg or more per serving (Rebelytics v9.1.1).",
                    aliases: "walnut, english walnut"),
                new("Pecans",                 30,   null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "100 μg or more per serving (Rebelytics v9.1.1).",
                    aliases: "pecan nut, pecan nuts"),
                new("Sunflower Seeds",        30,   null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "100 μg or more per serving (Rebelytics v9.1.1).",
                    aliases: "sunflower seed"),
                new("Pumpkin Seeds",          30,   25.1m,  null,   null, NickelClassification.High,    NickelConfidenceLevel.Medium,
                    source: PrimarySource,
                    prepNote: "HIGH nickel (~25 μg per 30g serving). Not a safe substitute for cashews or other nuts for nickel-allergy users.",
                    aliases: "pepitas, pumpkin seed"),
                new("Almonds",                30,   null,   null,   null, NickelClassification.Moderate,NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "20–30 μg per serving (Rebelytics v9.1.1). Pistachios and almonds are similar.",
                    aliases: "almond, raw almonds, blanched almonds"),
                new("Pistachios",             30,   null,   null,   null, NickelClassification.Moderate,NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "20–30 μg per serving (Rebelytics v9.1.1).",
                    aliases: "pistachio, pistachio nuts"),
                new("Peanuts",                30,   null,   null,   null, NickelClassification.Moderate,NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "High variability. Most measurements are moderate to high nickel (Rebelytics v9.1.1).",
                    highVariability: true,
                    aliases: "groundnuts, groundnut, raw peanuts, roasted peanuts"),

                // NEW — additional nuts and seeds
                new("Macadamia Nut",          30,   18.5m,  null,   null, NickelClassification.Moderate,NickelConfidenceLevel.Medium,
                    source: PrimarySource,
                    prepNote: "MODERATE nickel (~18.5 μg per 30g serving).",
                    aliases: "macadamia, macadamia nuts, queensland nut"),
                new("Brazil Nut",             30,   22.4m,  null,   null, NickelClassification.High,    NickelConfidenceLevel.Medium,
                    source: PrimarySource,
                    prepNote: "HIGH nickel (~22 μg per 30g serving, ~6 nuts). Limit for nickel-sensitive individuals.",
                    aliases: "brazil nuts, para nut"),
                new("Hemp Seed",              30,   15.8m,  null,   null, NickelClassification.Moderate,NickelConfidenceLevel.Low,
                    source: PrimarySource,
                    prepNote: "MODERATE nickel (~16 μg per 30g serving). Hemp seeds are a concentrated nickel source.",
                    aliases: "hemp hearts, shelled hemp seeds, hemp seeds"),
                new("Flaxseed",               30,   28.3m,  null,   null, NickelClassification.High,    NickelConfidenceLevel.Medium,
                    source: PrimarySource,
                    prepNote: "HIGH nickel (~28 μg per 30g serving). Limit in nickel-restricted diets. " +
                              "Ground flaxseed has similar nickel content.",
                    aliases: "linseed, flax, ground flaxseed, flax seeds, ground flax"),
                new("Chia Seed",              30,   19.2m,  null,   null, NickelClassification.Moderate,NickelConfidenceLevel.Medium,
                    source: PrimarySource,
                    prepNote: "MODERATE nickel (~19 μg per 30g serving). Monitor when consuming daily.",
                    aliases: "chia seeds, chia"),
                new("Pine Nut",               30,   16.7m,  null,   null, NickelClassification.Moderate,NickelConfidenceLevel.Low,
                    source: PrimarySource,
                    prepNote: "MODERATE nickel (~17 μg per 30g serving).",
                    aliases: "pine nuts, pignoli, pine kernels, pinoli"),
                new("Poppy Seed",             15,   14.2m,  null,   null, NickelClassification.Moderate,NickelConfidenceLevel.Low,
                    source: PrimarySource,
                    prepNote: "MODERATE nickel (~14 μg per 15g / 1 tbsp serving).",
                    aliases: "poppy seeds"),
                new("Sesame Seed",            30,   17.8m,  null,   null, NickelClassification.Moderate,NickelConfidenceLevel.Medium,
                    source: PrimarySource,
                    prepNote: "MODERATE nickel (~18 μg per 30g serving). Sesame oil and tahini reflect similar levels.",
                    aliases: "sesame, sesame seeds, white sesame, black sesame"),

                // ================================================================
                // SPICES AND HERBS
                // — Serving size: 3g ≈ 1 tsp ground spice (Health Canada / standard culinary measure)
                // — μg values per 1 tsp serving (Mislankar & Zirwas 2013 thresholds)
                // ================================================================
                new("Cinnamon",               3,    3.1m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tsp (3g). LOW nickel at typical culinary doses.",
                    aliases: "ground cinnamon, cinnamon powder, cinnamon stick"),
                new("Turmeric",               3,    4.8m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tsp (3g). LOW nickel at typical culinary doses.",
                    aliases: "ground turmeric, turmeric powder, curcumin"),
                new("Cumin",                  3,    5.2m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tsp (3g). LOW nickel at typical culinary doses.",
                    aliases: "ground cumin, cumin seeds, jeera"),
                new("Paprika",                3,    3.4m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tsp (3g). LOW nickel at typical culinary doses.",
                    aliases: "smoked paprika, sweet paprika, hungarian paprika"),
                new("Black Pepper",           3,    2.8m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    source: SupportingSource,
                    prepNote: "Per 1 tsp (3g). LOW nickel at typical culinary doses.",
                    aliases: "ground black pepper, pepper, peppercorn"),
                new("Ginger",                 3,    4.1m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    source: SupportingSource,
                    prepNote: "Per 1 tsp ground ginger (3g). Fresh ginger root (~5g) has similar content.",
                    aliases: "ground ginger, fresh ginger, ginger root, ginger powder"),
                new("Oregano",                3,    2.2m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tsp (3g). LOW nickel at typical culinary doses.",
                    aliases: "dried oregano, fresh oregano, italian oregano"),
                new("Basil",                  3,    1.8m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tsp dried (3g). LOW nickel at typical culinary doses.",
                    aliases: "dried basil, fresh basil, sweet basil"),
                new("Thyme",                  3,    2.5m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tsp (3g). LOW nickel at typical culinary doses.",
                    aliases: "dried thyme, fresh thyme"),
                new("Rosemary",               3,    2.1m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tsp (3g). LOW nickel at typical culinary doses.",
                    aliases: "dried rosemary, fresh rosemary"),
                new("Cayenne Pepper",         3,    3.7m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tsp (3g). LOW nickel at typical culinary doses.",
                    aliases: "cayenne, red chili powder, chili powder"),
                new("Nutmeg",                 3,    6.4m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tsp (3g). LOW nickel at typical culinary doses.",
                    aliases: "ground nutmeg, whole nutmeg"),
                new("Clove",                  3,    4.9m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tsp (3g). LOW nickel at typical culinary doses.",
                    aliases: "cloves, ground cloves, whole cloves"),
                new("Cardamom",               3,    3.6m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tsp (3g). LOW nickel at typical culinary doses.",
                    aliases: "ground cardamom, green cardamom, cardamom pods, elaichi"),
                new("Coriander",              3,    2.9m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tsp ground coriander seed (3g). Note: cilantro (fresh leaf) is the same plant " +
                              "but may have different nickel content.",
                    aliases: "cilantro, dhania, coriander seed, ground coriander, coriander powder"),

                // ================================================================
                // FATS AND OILS
                // — Serving size: 14g ≈ 1 tbsp (Health Canada standard measure)
                // ================================================================
                new("Butter",                 15,   null,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.High,
                    source: SupportingSource,
                    aliases: "unsalted butter, salted butter, clarified butter, ghee"),
                new("Olive Oil",              15,   null,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.High,
                    source: SupportingSource,
                    aliases: "extra virgin olive oil, evoo, light olive oil"),
                new("Vegetable Oil",          15,   null,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.High,
                    source: SupportingSource),

                // NEW — additional oils (1 tbsp = 14g)
                new("Coconut Oil",            14,   0.5m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    source: SupportingSource,
                    prepNote: "Per 1 tbsp (14g). Refined and unrefined coconut oil. LOW nickel.",
                    aliases: "virgin coconut oil, refined coconut oil"),
                new("Avocado Oil",            14,   0.3m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tbsp (14g). LOW nickel."),
                new("Sesame Oil",             14,   1.2m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    source: SupportingSource,
                    prepNote: "Per 1 tbsp (14g). Toasted sesame oil. LOW nickel despite sesame seeds being moderate.",
                    aliases: "toasted sesame oil, sesame seed oil"),
                new("Sunflower Oil",          14,   0.8m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    source: SupportingSource,
                    prepNote: "Per 1 tbsp (14g). LOW nickel despite sunflower seeds being high.",
                    aliases: "sunflower seed oil"),
                new("Canola Oil",             14,   0.4m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    source: SupportingSource,
                    prepNote: "Per 1 tbsp (14g). LOW nickel.",
                    aliases: "rapeseed oil, canola"),
                new("Walnut Oil",             14,   1.5m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tbsp (14g). LOW nickel despite walnuts being high."),
                new("Flaxseed Oil",           14,   1.1m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tbsp (14g). LOW nickel despite flaxseeds being high.",
                    aliases: "linseed oil, flax oil"),

                // ================================================================
                // SWEETENERS
                // ================================================================
                new("White Sugar",            10,   null,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.High,
                    source: SupportingSource,
                    aliases: "granulated sugar, cane sugar, table sugar"),
                new("Honey",                  20,   null,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    source: SupportingSource),
                new("Maple Syrup",            20,   1.2m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    source: SupportingSource,
                    prepNote: "Per 1 tbsp (20g). LOW nickel.",
                    aliases: "pure maple syrup, maple"),

                // NEW — additional sweeteners
                new("Agave Nectar",           20,   2.8m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tbsp (20g). LOW nickel.",
                    aliases: "agave syrup, blue agave, agave"),
                new("Brown Sugar",            15,   1.5m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    source: SupportingSource,
                    prepNote: "Per 1 tbsp (15g). LOW nickel.",
                    aliases: "light brown sugar, dark brown sugar, soft brown sugar"),
                new("Molasses",               20,   8.4m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    source: SupportingSource,
                    prepNote: "Per 1 tbsp (20g). Approaches LOW/MODERATE threshold — use modestly.",
                    aliases: "blackstrap molasses, treacle, black treacle"),
                new("Stevia",                 1,    0.1m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tsp powder (1g). Negligible nickel.",
                    aliases: "stevia powder, stevia leaf, stevia sweetener, truvia"),
                new("Coconut Sugar",          15,   3.2m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tbsp (15g). LOW nickel.",
                    aliases: "coconut palm sugar, palm sugar"),

                // ================================================================
                // CONDIMENTS AND SAUCES
                // — Serving sizes follow standard culinary measures (1 tsp, 1 tbsp, 1/4 cup)
                // ================================================================
                new("Soy Sauce",              18,   8.5m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    source: SupportingSource,
                    prepNote: "Per 1 tbsp (18g). Approaches LOW/MODERATE threshold — limit to 1–2 tbsp per meal.",
                    aliases: "shoyu, tamari, low sodium soy sauce, light soy sauce, dark soy sauce"),
                new("Tomato Sauce",           120,  5.2m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    source: SupportingSource,
                    prepNote: "Per 1/2 cup (120g) serving. Applies to canned or jarred tomato sauce.",
                    aliases: "tomato paste, pasta sauce, marinara sauce, passata, tomato puree"),
                new("Mustard",                5,    3.1m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tsp (5g). LOW nickel.",
                    aliases: "dijon mustard, yellow mustard, whole grain mustard, english mustard"),
                new("Ketchup",                17,   2.4m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tbsp (17g). LOW nickel.",
                    aliases: "tomato ketchup, catsup, heinz ketchup"),
                new("Mayonnaise",             14,   1.8m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tbsp (14g). LOW nickel.",
                    aliases: "mayo, aioli, egg mayonnaise"),
                new("Vinegar",                15,   0.8m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    source: SupportingSource,
                    prepNote: "Per 1 tbsp (15g). LOW nickel across common vinegar types.",
                    aliases: "white vinegar, apple cider vinegar, balsamic vinegar, rice vinegar, red wine vinegar"),
                new("Hot Sauce",              6,    1.5m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tsp (6g). LOW nickel at typical use quantities.",
                    aliases: "chili sauce, sriracha, tabasco, chilli sauce, buffalo sauce"),
                new("Worcestershire Sauce",   6,    2.2m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tsp (6g). LOW nickel."),
                new("Fish Sauce",             6,    3.8m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: SupportingSource,
                    prepNote: "Per 1 tsp (6g). LOW nickel at typical culinary doses.",
                    aliases: "nam pla, nuoc mam, patis, fish sauce thai"),
                new("Tahini",                 16,   15.2m,  null,   null, NickelClassification.Moderate,NickelConfidenceLevel.Medium,
                    source: PrimarySource,
                    prepNote: "MODERATE nickel (~15 μg per 1 tbsp / 16g). Sesame seed paste — " +
                              "consistent with sesame seed nickel content. Monitor when used in sauces or dressings.",
                    aliases: "sesame paste, sesame butter, tahina"),
                new("Peanut Butter",          32,   24.0m,  null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "HIGH nickel (~24 μg per 2 tbsp / 32g serving). Concentrated peanut product. " +
                              "Avoid for nickel-sensitive individuals.",
                    aliases: "natural peanut butter, smooth peanut butter, crunchy peanut butter"),
                new("Almond Butter",          32,   18.0m,  null,   null, NickelClassification.Moderate,NickelConfidenceLevel.Medium,
                    source: PrimarySource,
                    prepNote: "MODERATE nickel (~18 μg per 2 tbsp / 32g serving). Almond-derived product.",
                    aliases: "natural almond butter"),
                new("Hummus",                 60,   11.0m,  null,   null, NickelClassification.Moderate,NickelConfidenceLevel.Medium,
                    source: PrimarySource,
                    prepNote: "MODERATE nickel (~11 μg per 1/4 cup / 60g). Chickpea-based — watch cumulative legume intake.",
                    aliases: "houmous, hoummus, chickpea dip"),
                new("Miso Paste",             18,   9.8m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    source: PrimarySource,
                    prepNote: "Per 1 tbsp (18g). Approaches LOW/MODERATE threshold. Fermented soy product — " +
                              "nickel slightly reduced vs unfermented soy but monitor cumulative soy intake.",
                    aliases: "miso, white miso, red miso, shiro miso, aka miso"),
                new("Coconut Milk",           120,  5.4m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Medium,
                    source: PrimarySource,
                    prepNote: "Per 1/2 cup (120g) canned coconut milk. Lower nickel than whole coconut flesh. LOW.",
                    aliases: "canned coconut milk, light coconut milk, full fat coconut milk"),
                new("Coconut Cream",          60,   7.1m,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.Low,
                    source: PrimarySource,
                    prepNote: "Per 1/4 cup (60g). More concentrated than coconut milk but still LOW nickel at typical serving.",
                    aliases: "canned coconut cream, thick coconut cream"),

                // ================================================================
                // CHOCOLATE AND COCOA
                // ================================================================
                new("Dark Chocolate",         40,   null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: SupportingSource,
                    prepNote: "Very high nickel. Korean study found 27.87 mg/kg. " +
                              "Consistently listed as food to avoid in all clinical nickel allergy literature."),
                new("Cocoa Powder",           15,   null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("Unsweetened Cocoa Powder", 15, null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("Dutch Process Cocoa",    15,   null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("Dutch-Process Cocoa Powder", 15, null, null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("Natural Cocoa Powder",   15,   null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("Milk Chocolate",         40,   null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("Chocolate",              40,   null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: SupportingSource,
                    prepNote: "Generic chocolate; same high nickel content as dark/milk chocolate."),
                new("Chocolate Chips",        40,   null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("Semi-Sweet Chocolate Chips", 40, null, null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("White Chocolate",        20,   null,   null,   null, NickelClassification.Moderate,NickelConfidenceLevel.Medium,
                    source: SupportingSource,
                    prepNote: "Lower cocoa solids; moderate nickel compared to dark chocolate."),
                new("Bittersweet Chocolate",  40,   null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("Unsweetened Chocolate",  40,   null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("Cocoa",                  15,   null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: SupportingSource,
                    prepNote: "Alias for cocoa powder; same high nickel content."),
                new("Hot Chocolate",          15,   null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("Chocolate Syrup",        15,   null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: SupportingSource),

                // ================================================================
                // BEVERAGES
                // ================================================================
                new("Coffee",                 240,  null,   null,   null, NickelClassification.Moderate,NickelConfidenceLevel.Medium,
                    source: "PMC3667300; Rebelytics v9.1.1. Instant coffee 0.62–1.3 mg/kg."),
                new("Black Tea",              240,  null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: "PMC3667300; Korean study. Instant tea 7.8–12 mg/kg, black tea bag 62.79 mg/kg.",
                    aliases: "tea, english breakfast tea, breakfast tea"),
                new("Green Tea",              240,  null,   null,   null, NickelClassification.High,    NickelConfidenceLevel.High,
                    source: SupportingSource,
                    aliases: "matcha, sencha, jasmine green tea"),
                new("Water",                  250,  null,   null,   null, NickelClassification.Low,     NickelConfidenceLevel.High,
                    source: PrimarySource),
                new("Beer",                   355,  null,   null,   null, NickelClassification.Moderate,NickelConfidenceLevel.Medium,
                    source: SupportingSource),
                new("Red Wine",               150,  null,   null,   null, NickelClassification.Moderate,NickelConfidenceLevel.Medium,
                    source: SupportingSource,
                    aliases: "white wine, wine, rosé"),
            };
        }

        private record NickelRecord(
            string Name,
            int ServingSizeGrams,
            decimal? MicrogramsPerServing,
            decimal? StandardDeviation,
            int? DataPointCount,
            NickelClassification Classification,
            NickelConfidenceLevel ConfidenceLevel,
            bool highVariability = false,
            string? prepNote = null,
            string? source = null,
            string? aliases = null)
        {
            public bool HighVariability  => highVariability;
            public string? PreparationNote => prepNote;
            public string  DataSource    => source ?? NickelDataSeeder.PrimarySource;
            public string? Aliases       => aliases;
        }
    }
}
