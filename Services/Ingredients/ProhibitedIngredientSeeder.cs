using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Ingredients
{
    /// <summary>
    /// Seeds the database with initial prohibited ingredients.
    /// Includes haram items, dangerous substances, and illegal items.
    /// </summary>
    public class ProhibitedIngredientSeeder
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<ProhibitedIngredientSeeder> _logger;

        public ProhibitedIngredientSeeder(
            PlaterlyDbContext db,
            ILogger<ProhibitedIngredientSeeder> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task SeedAsync(CancellationToken ct = default)
        {
            // Check if already seeded
            if (await _db.ProhibitedIngredients.AnyAsync(ct))
            {
                _logger.LogInformation("Prohibited ingredients already seeded, skipping");
                return;
            }

            var prohibited = new List<ProhibitedIngredient>();

            // ========== HARAM INGREDIENTS ==========
            // Pork and pork derivatives
            prohibited.AddRange(CreateHaramItems(new[]
            {
                ("pork", "Pork is not halal."),
                ("bacon", "Bacon (pork) is not halal."),
                ("ham", "Ham (pork) is not halal."),
                ("pig", "Pig products are not halal."),
                ("swine", "Swine products are not halal."),
                ("lard", "Lard (pig fat) is not halal."),
                ("prosciutto", "Prosciutto (pork) is not halal."),
                ("pancetta", "Pancetta (pork) is not halal."),
                ("chorizo", "Chorizo may contain pork and is not halal."),
                ("salami", "Salami may contain pork and is not halal."),
                ("pepperoni", "Pepperoni may contain pork and is not halal."),
                ("sausage", "Sausage may contain pork. Please use halal-certified sausage."),
                ("hot dog", "Hot dogs may contain pork. Please use halal-certified."),
                ("gelatin", "Gelatin may be pork-derived. Please use halal gelatin."),
                ("collagen", "Collagen may be pork-derived. Please use halal sources."),
            }));

            // Alcohol
            prohibited.AddRange(CreateHaramItems(new[]
            {
                ("wine", "Wine (alcohol) is not halal."),
                ("beer", "Beer (alcohol) is not halal."),
                ("whiskey", "Whiskey (alcohol) is not halal."),
                ("whisky", "Whisky (alcohol) is not halal."),
                ("vodka", "Vodka (alcohol) is not halal."),
                ("rum", "Rum (alcohol) is not halal."),
                ("gin", "Gin (alcohol) is not halal."),
                ("brandy", "Brandy (alcohol) is not halal."),
                ("cognac", "Cognac (alcohol) is not halal."),
                ("tequila", "Tequila (alcohol) is not halal."),
                ("sake", "Sake (alcohol) is not halal."),
                ("champagne", "Champagne (alcohol) is not halal."),
                ("liqueur", "Liqueur (alcohol) is not halal."),
                ("liquor", "Liquor (alcohol) is not halal."),
                ("bourbon", "Bourbon (alcohol) is not halal."),
                ("scotch", "Scotch (alcohol) is not halal."),
                ("mirin", "Mirin contains alcohol. Use halal mirin substitute."),
                ("cooking wine", "Cooking wine is not halal."),
                ("sherry", "Sherry (alcohol) is not halal."),
                ("port wine", "Port wine (alcohol) is not halal."),
                ("marsala", "Marsala wine is not halal."),
            }));

            // ========== DANGEROUS/HARMFUL ITEMS ==========
            prohibited.AddRange(CreateDangerousItems(new[]
            {
                ("bleach", "Bleach is a dangerous chemical, not a food ingredient."),
                ("ammonia", "Ammonia is a dangerous chemical, not a food ingredient."),
                ("gasoline", "Gasoline is a dangerous chemical, not a food ingredient."),
                ("petrol", "Petrol is a dangerous chemical, not a food ingredient."),
                ("kerosene", "Kerosene is a dangerous chemical, not a food ingredient."),
                ("antifreeze", "Antifreeze is a dangerous chemical, not a food ingredient."),
                ("detergent", "Detergent is not a food ingredient."),
                ("soap", "Soap is not a food ingredient."),
                ("cleaning fluid", "Cleaning fluid is not a food ingredient."),
                ("drain cleaner", "Drain cleaner is a dangerous chemical."),
                ("pesticide", "Pesticide is dangerous and not a food ingredient."),
                ("insecticide", "Insecticide is dangerous and not a food ingredient."),
                ("rat poison", "Rat poison is dangerous and not a food ingredient."),
                ("arsenic", "Arsenic is a poison and not a food ingredient."),
                ("cyanide", "Cyanide is a poison and not a food ingredient."),
                ("mercury", "Mercury is a toxic substance and not a food ingredient."),
                ("lead", "Lead is a toxic substance and not a food ingredient."),
                ("turpentine", "Turpentine is a dangerous chemical."),
                ("acetone", "Acetone is a dangerous chemical."),
                ("methanol", "Methanol is a dangerous chemical."),
                ("formaldehyde", "Formaldehyde is a dangerous chemical."),
                ("motor oil", "Motor oil is not a food ingredient."),
                ("paint", "Paint is not a food ingredient."),
                ("glue", "Glue is not a food ingredient."),
            }));

            // ========== ILLEGAL SUBSTANCES ==========
            prohibited.AddRange(CreateIllegalItems(new[]
            {
                ("marijuana", "Marijuana is an illegal substance in most jurisdictions."),
                ("cannabis", "Cannabis is an illegal substance in most jurisdictions."),
                ("weed", "Weed (cannabis) is an illegal substance."),
                ("cocaine", "Cocaine is an illegal substance."),
                ("heroin", "Heroin is an illegal substance."),
                ("meth", "Methamphetamine is an illegal substance."),
                ("methamphetamine", "Methamphetamine is an illegal substance."),
                ("ecstasy", "Ecstasy is an illegal substance."),
                ("mdma", "MDMA is an illegal substance."),
                ("lsd", "LSD is an illegal substance."),
                ("psilocybin", "Psilocybin is an illegal substance."),
                ("magic mushroom", "Magic mushrooms contain illegal substances."),
                ("opium", "Opium is an illegal substance."),
                ("ketamine", "Ketamine is a controlled substance."),
                ("pcp", "PCP is an illegal substance."),
                ("crack", "Crack cocaine is an illegal substance."),
                ("fentanyl", "Fentanyl is a controlled substance."),
            }));

            // ========== HARMFUL/INAPPROPRIATE ==========
            prohibited.AddRange(CreateHarmfulItems(new[]
            {
                ("blood", "Human blood is not an appropriate food ingredient."),
                ("human", "Human-derived ingredients are not allowed."),
                ("feces", "Feces is not a food ingredient."),
                ("urine", "Urine is not a food ingredient."),
                ("vomit", "Vomit is not a food ingredient."),
                ("roadkill", "Roadkill is not a safe food source."),
            }));

            await _db.ProhibitedIngredients.AddRangeAsync(prohibited, ct);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Seeded {Count} prohibited ingredients", prohibited.Count);
        }

        private static List<ProhibitedIngredient> CreateHaramItems(IEnumerable<(string keyword, string reason)> items)
        {
            return items.Select(i => new ProhibitedIngredient
            {
                Keyword = i.keyword,
                Category = ProhibitedCategory.Haram,
                MatchType = Models.MatchType.Contains,
                Reason = i.reason,
                Severity = ViolationSeverity.Block,
                IsActive = true
            }).ToList();
        }

        private static List<ProhibitedIngredient> CreateDangerousItems(IEnumerable<(string keyword, string reason)> items)
        {
            return items.Select(i => new ProhibitedIngredient
            {
                Keyword = i.keyword,
                Category = ProhibitedCategory.Dangerous,
                MatchType = Models.MatchType.Contains,
                Reason = i.reason,
                Severity = ViolationSeverity.BlockAndFlag,
                IsActive = true
            }).ToList();
        }

        private static List<ProhibitedIngredient> CreateIllegalItems(IEnumerable<(string keyword, string reason)> items)
        {
            return items.Select(i => new ProhibitedIngredient
            {
                Keyword = i.keyword,
                Category = ProhibitedCategory.Illegal,
                MatchType = Models.MatchType.Contains,
                Reason = i.reason,
                Severity = ViolationSeverity.BlockAndFlag,
                IsActive = true
            }).ToList();
        }

        private static List<ProhibitedIngredient> CreateHarmfulItems(IEnumerable<(string keyword, string reason)> items)
        {
            return items.Select(i => new ProhibitedIngredient
            {
                Keyword = i.keyword,
                Category = ProhibitedCategory.Harmful,
                MatchType = Models.MatchType.Contains,
                Reason = i.reason,
                Severity = ViolationSeverity.BlockAndFlag,
                IsActive = true
            }).ToList();
        }
    }
}
