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

        public async Task SeedAsync(CancellationToken ct = default)
        {
            var records = GetNickelData();
            int updated = 0;
            int created = 0;

            foreach (var record in records)
            {
                var normalized = record.Name.ToLowerInvariant().Trim();

                var ingredient = await _db.GlobalIngredients
                    .FirstOrDefaultAsync(g =>
                        g.NormalizedName == normalized ||
                        (g.Aliases != null && g.Aliases.ToLower().Contains(normalized)), ct);

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
                    created++;
                }
                else
                {
                    updated++;
                }

                ingredient.NickelMicrogramsPerServing = record.MicrogramsPerServing;
                ingredient.NickelServingSizeGrams = record.ServingSizeGrams;
                ingredient.NickelClassification = record.Classification;
                ingredient.NickelStandardDeviation = record.StandardDeviation;
                ingredient.NickelDataPointCount = record.DataPointCount;
                ingredient.NickelConfidenceLevel = record.ConfidenceLevel;
                ingredient.NickelDataSource = PrimarySource;
                ingredient.NickelDataSourceYear = 2025;
                ingredient.NickelHighVariability = record.HighVariability;
                ingredient.NickelPreparationNote = record.PreparationNote;
                ingredient.UpdatedAt = DateTime.UtcNow;
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
                new("Whole Wheat Pasta Cooked", 215, 11.77m, 8.86m, 8,  NickelClassification.Moderate, NickelConfidenceLevel.Medium,
                    source: PrimarySource,
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
                    source: PrimarySource,
                    prepNote: "Raw oats. Same HIGH nickel classification as rolled/regular oats (Rebelytics v9.1.1)."),
                new("Soy Flour",              15,   64.75m, 28.47m, 7,  NickelClassification.High,     NickelConfidenceLevel.Medium),

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
                new("Tangerine",              140,  8.65m,  7.11m,  11, NickelClassification.Low,      NickelConfidenceLevel.Medium),

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
                new("Peach",                  140,  10.34m, 4.42m,  155,NickelClassification.Moderate, NickelConfidenceLevel.High),
                new("Melon",                  150,  11.80m, 8.14m,  106,NickelClassification.Moderate, NickelConfidenceLevel.High),
                new("Avocado",                140,  42.44m, 39.82m, 85, NickelClassification.High,     NickelConfidenceLevel.High),
                new("Coconut",                140,  100.10m,54.41m, 4,  NickelClassification.High,     NickelConfidenceLevel.Low),

                // ================================================================
                // VEGETABLES — ROOT
                // ================================================================
                new("Onion",                  85,   3.06m,  2.33m,  92, NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Carrot",                 85,   3.79m,  2.36m,  103,NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Radish",                 85,   4.26m,  1.75m,  13, NickelClassification.Low,      NickelConfidenceLevel.Medium),
                new("Rutabaga",               85,   4.81m,  1.21m,  7,  NickelClassification.Low,      NickelConfidenceLevel.Medium),
                new("Turnip",                 85,   5.40m,  3.76m,  4,  NickelClassification.Low,      NickelConfidenceLevel.Low),
                new("Beets",                  85,   8.46m,  7.19m,  16, NickelClassification.Low,      NickelConfidenceLevel.Medium),
                new("Potato",                 110,  8.17m,  9.50m,  84, NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Sweet Potato",           110,  12.55m, 7.67m,  9,  NickelClassification.Moderate, NickelConfidenceLevel.Medium),
                new("Yam",                    110,  17.60m, 4.47m,  4,  NickelClassification.Moderate, NickelConfidenceLevel.Low),
                new("Parsnip",                85,   19.72m, 7.28m,  9,  NickelClassification.Moderate, NickelConfidenceLevel.Medium),
                new("Fennel",                 85,   28.90m, null,   2,  NickelClassification.High,     NickelConfidenceLevel.Low),
                new("Cassava",                110,  32.63m, 25.99m, 3,  NickelClassification.High,     NickelConfidenceLevel.Low),

                // ================================================================
                // VEGETABLES — LEAFY
                // ================================================================
                new("Cabbage",                85,   4.80m,  4.29m,  59, NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Lettuce",                85,   5.83m,  4.70m,  124,NickelClassification.Low,      NickelConfidenceLevel.High),
                new("Iceberg Lettuce",        85,   9.01m,  6.86m,  83, NickelClassification.Low,      NickelConfidenceLevel.High),
                // Spinach: Mean 8.94 μg = LOW classification, but SD of 9.99 is extremely high
                // (some samples reached 51 μg). High variability flag required.
                // Corrects prior codebase label of "High nickel" which was inaccurate.
                new("Spinach",                85,   8.94m,  9.99m,  45, NickelClassification.Low,      NickelConfidenceLevel.High,
                    highVariability: true,
                    prepNote: "Mean nickel is LOW but variability is extremely high (SD 9.99, max 51 μg observed). " +
                              "Some servings may reach MODERATE or HIGH levels. " +
                              "Nickel-sensitive individuals should consume with caution."),
                new("Kale",                   85,   11.79m, 1.66m,  6,  NickelClassification.Moderate, NickelConfidenceLevel.Medium),

                // ================================================================
                // MEATS AND EGGS
                // — Source: Sharma A.D. (2013). Animal tissues generally contain less nickel
                //   than plant tissues. Meat, poultry, and eggs are suitable for low-nickel diet.
                // ================================================================
                new("Chicken Breast",         120,  null,   null,   null, NickelClassification.Low,    NickelConfidenceLevel.High,
                    source: SupportingSource,
                    prepNote: "Animal proteins are consistently low nickel across all clinical literature."),
                new("Beef",                   120,  null,   null,   null, NickelClassification.Low,    NickelConfidenceLevel.Medium,
                    source: SupportingSource),
                new("Pork",                   120,  null,   null,   null, NickelClassification.Low,    NickelConfidenceLevel.Medium,
                    source: SupportingSource),
                new("Eggs",                   50,   null,   null,   null, NickelClassification.Low,    NickelConfidenceLevel.High,
                    source: SupportingSource,
                    prepNote: "Suitable for low-nickel diet per Sharma A.D. (2013)."),

                // ================================================================
                // FISH AND SEAFOOD
                // ================================================================
                new("Fish",                   120,  null,   null,   null, NickelClassification.Low,    NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "Approximately 85% of raw and cooked fish samples are low nickel (Rebelytics v9.1.1)."),
                new("Shrimp",                 120,  null,   null,   null, NickelClassification.Low,    NickelConfidenceLevel.Medium,
                    source: PrimarySource,
                    prepNote: "94% of cooked crustacean samples are low nickel (Rebelytics v9.1.1)."),
                new("Oysters",                120,  null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "Bivalves (oysters, clams, mussels, scallops): only 10% of raw samples are low nickel."),
                new("Tuna",                   120,  null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.Medium,
                    source: SupportingSource,
                    prepNote: "Listed as high-nickel food to avoid in multiple clinical studies."),

                // ================================================================
                // BEANS AND LEGUMES
                // ================================================================
                new("Lentils",                175,  null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "UK study found lentils 1.6–2.3 mg/kg fresh weight (PMC3667300). " +
                              "Canned, drained and rinsed lentils have reduced nickel content."),
                new("Lentils Cooked",         175,  null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "Cooked lentils remain HIGH nickel. Draining and rinsing reduces nickel content. " +
                              "UK study: 1.6–2.3 mg/kg fresh weight (PMC3667300)."),
                new("Chickpeas",              175,  null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "Canned, drained and rinsed chickpeas have reduced nickel content."),
                new("Chickpeas Cooked",       175,  null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "Cooked chickpeas remain HIGH nickel. Draining and rinsing reduces nickel content."),
                new("Soybeans",               175,  null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: PrimarySource),
                new("Soybeans Cooked",        175,  null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "Cooked soybeans remain HIGH nickel."),
                new("Black Beans",            175,  null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "Canned, drained and rinsed beans have reduced nickel content."),
                new("Kidney Beans",           175,  null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "Canned, drained and rinsed beans have reduced nickel content."),
                new("Peas",                   85,   null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.Medium,
                    source: PrimarySource),

                // ================================================================
                // NUTS AND SEEDS
                // ================================================================
                new("Cashews",                30,   100m,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "≥100 μg per 30g serving. Walnuts, cashews, pecans, and sunflower seeds " +
                              "all exceed 100 μg per serving (Rebelytics v9.1.1 September 2025)."),
                new("Walnuts",                30,   null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "100 μg or more per serving (Rebelytics v9.1.1)."),
                new("Pecans",                 30,   null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "100 μg or more per serving (Rebelytics v9.1.1)."),
                new("Sunflower Seeds",        30,   null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "100 μg or more per serving (Rebelytics v9.1.1)."),
                new("Pumpkin Seeds",          30,   null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "High nickel. Not a safe substitute for cashews or other nuts for nickel-allergy users."),
                new("Almonds",                30,   null,   null,   null, NickelClassification.Moderate,NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "20–30 μg per serving (Rebelytics v9.1.1). Pistachios and almonds are similar."),
                new("Pistachios",             30,   null,   null,   null, NickelClassification.Moderate,NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "20–30 μg per serving (Rebelytics v9.1.1)."),
                new("Peanuts",                30,   null,   null,   null, NickelClassification.Moderate,NickelConfidenceLevel.High,
                    source: PrimarySource,
                    prepNote: "High variability. Most measurements are moderate to high nickel (Rebelytics v9.1.1).",
                    highVariability: true),

                // ================================================================
                // CHOCOLATE AND COCOA
                // ================================================================
                new("Dark Chocolate",         40,   null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: SupportingSource,
                    prepNote: "Very high nickel. Korean study found 27.87 mg/kg. " +
                              "Consistently listed as food to avoid in all clinical nickel allergy literature."),
                new("Cocoa Powder",           15,   null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("Unsweetened Cocoa Powder", 15, null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("Dutch Process Cocoa",    15,   null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("Dutch-Process Cocoa Powder", 15, null, null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("Natural Cocoa Powder",   15,   null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("Milk Chocolate",         40,   null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("Chocolate",              40,   null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: SupportingSource,
                    prepNote: "Generic chocolate; same high nickel content as dark/milk chocolate."),
                new("Chocolate Chips",        40,   null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("Semi-Sweet Chocolate Chips", 40, null, null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("White Chocolate",        20,   null,   null,   null, NickelClassification.Moderate, NickelConfidenceLevel.Medium,
                    source: SupportingSource,
                    prepNote: "Lower cocoa solids; moderate nickel compared to dark chocolate."),
                new("Bittersweet Chocolate",  40,   null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("Unsweetened Chocolate",  40,   null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("Cocoa",                  15,   null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: SupportingSource,
                    prepNote: "Alias for cocoa powder; same high nickel content."),
                new("Hot Chocolate",          15,   null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("Chocolate Syrup",        15,   null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: SupportingSource),

                // ================================================================
                // BEVERAGES
                // ================================================================
                new("Coffee",                 240,  null,   null,   null, NickelClassification.Moderate, NickelConfidenceLevel.Medium,
                    source: "PMC3667300; Rebelytics v9.1.1. Instant coffee 0.62–1.3 mg/kg."),
                new("Black Tea",              240,  null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: "PMC3667300; Korean study. Instant tea 7.8–12 mg/kg, black tea bag 62.79 mg/kg."),
                new("Green Tea",              240,  null,   null,   null, NickelClassification.High,   NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("Water",                  250,  null,   null,   null, NickelClassification.Low,    NickelConfidenceLevel.High,
                    source: PrimarySource),
                new("Beer",                   355,  null,   null,   null, NickelClassification.Moderate, NickelConfidenceLevel.Medium,
                    source: SupportingSource),
                new("Red Wine",               150,  null,   null,   null, NickelClassification.Moderate, NickelConfidenceLevel.Medium,
                    source: SupportingSource),

                // ================================================================
                // FATS AND OILS
                // ================================================================
                new("Butter",                 15,   null,   null,   null, NickelClassification.Low,    NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("Olive Oil",              15,   null,   null,   null, NickelClassification.Low,    NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("Vegetable Oil",          15,   null,   null,   null, NickelClassification.Low,    NickelConfidenceLevel.High,
                    source: SupportingSource),

                // ================================================================
                // SWEETENERS
                // ================================================================
                new("White Sugar",            10,   null,   null,   null, NickelClassification.Low,    NickelConfidenceLevel.High,
                    source: SupportingSource),
                new("Honey",                  20,   null,   null,   null, NickelClassification.Low,    NickelConfidenceLevel.Medium,
                    source: SupportingSource),
                new("Maple Syrup",            20,   null,   null,   null, NickelClassification.Low,    NickelConfidenceLevel.Medium,
                    source: SupportingSource),
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
            string? source = null)
        {
            public bool HighVariability => highVariability;
            public string? PreparationNote => prepNote;
            public string DataSource => source ?? NickelDataSeeder.PrimarySource;
        }
    }
}
