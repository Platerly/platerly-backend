using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Ingredients
{
    /// <summary>
    /// Seeds common ingredient substitutions for allergies and dietary preferences.
    /// </summary>
    public class IngredientSubstitutionSeeder
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<IngredientSubstitutionSeeder> _logger;

        public IngredientSubstitutionSeeder(
            PlaterlyDbContext db,
            ILogger<IngredientSubstitutionSeeder> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Seeds allergen and dietary-preference ingredient substitutions.
        /// Skips when 300 or more <c>IngredientSubstitutions</c> rows already exist, unless
        /// <paramref name="force"/> is <c>true</c>. Entries are matched by original + substitute
        /// ingredient ID pair and only inserted if the pair does not already exist.
        /// </summary>
        /// <param name="force">When <c>true</c>, bypasses the row-count guard and runs even if the DB is already seeded.</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task SeedAsync(bool force = false, CancellationToken ct = default)
        {
            // Check if already seeded (expect ~78 substitutions)
            var existingCount = await _db.IngredientSubstitutions.CountAsync(ct);
            if (!force && existingCount >= 300)
            {
                _logger.LogInformation("Ingredient substitutions already seeded ({Count} found), skipping", existingCount);
                return;
            }

            if (force)
            {
                _logger.LogInformation("Force reseed requested — clearing {Count} existing substitutions", existingCount);
                _db.IngredientSubstitutions.RemoveRange(_db.IngredientSubstitutions);
                await _db.SaveChangesAsync(ct);
            }

            // Get or create all needed ingredients first
            var ingredientCache = new Dictionary<string, Guid>();

            // Define all substitutions
            var substitutionData = GetSubstitutionData();

            // Collect all unique ingredient names
            var allNames = substitutionData
                .SelectMany(s => new[] { s.Original, s.Substitute })
                .Distinct()
                .ToList();

            // Ensure all ingredients exist
            foreach (var name in allNames)
            {
                var ingredient = await GetOrCreateIngredientAsync(name, ct);
                ingredientCache[name.ToLowerInvariant()] = ingredient.Id;
            }

            // Get allergy IDs for linking
            var allergies = await _db.Allergies.ToDictionaryAsync(a => a.Name.ToLower(), a => a.Id, ct);

            // Build substitutions, deduplicating by (OriginalId, SubstituteId) pair
            var seen = new HashSet<(Guid, Guid)>();
            var substitutions = new List<IngredientSubstitution>();

            foreach (var data in substitutionData)
            {
                var originalId = ingredientCache[data.Original.ToLowerInvariant()];
                var substituteId = ingredientCache[data.Substitute.ToLowerInvariant()];

                // Skip duplicates within seed data
                if (!seen.Add((originalId, substituteId)))
                {
                    _logger.LogDebug("Skipping duplicate seed pair: {Original} -> {Substitute}", data.Original, data.Substitute);
                    continue;
                }

                Guid? allergyId = null;
                if (!string.IsNullOrEmpty(data.AllergyName) && allergies.TryGetValue(data.AllergyName.ToLower(), out var aId))
                {
                    allergyId = aId;
                }

                substitutions.Add(new IngredientSubstitution
                {
                    Id = Guid.NewGuid(),
                    OriginalIngredientId = originalId,
                    SubstituteIngredientId = substituteId,
                    Reason = data.Reason,
                    AllergyId = allergyId,
                    ConversionRatio = data.Ratio,
                    Notes = data.Notes,
                    ConfidenceScore = 85,
                    PositiveVotes = 10,
                    NegativeVotes = 0,
                    UsageCount = 0,
                    IsVerified = true,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _db.IngredientSubstitutions.AddRangeAsync(substitutions, ct);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Seeded {Count} ingredient substitutions", substitutions.Count);
        }

        private async Task<GlobalIngredient> GetOrCreateIngredientAsync(string name, CancellationToken ct)
        {
            var normalized = name.Trim().ToLowerInvariant();

            var existing = await _db.GlobalIngredients
                .FirstOrDefaultAsync(g => g.NormalizedName == normalized, ct);

            if (existing != null)
                return existing;

            var ingredient = new GlobalIngredient
            {
                Id = Guid.NewGuid(),
                Name = name.Trim(),
                NormalizedName = normalized,
                IsActive = true,
                IsVerified = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.GlobalIngredients.Add(ingredient);
            await _db.SaveChangesAsync(ct);

            return ingredient;
        }

        private static List<SubstitutionSeedData> GetSubstitutionData()
        {
            return new List<SubstitutionSeedData>
            {
                // ========== DAIRY — MILK ==========
                new("Milk", "Oat Milk", SubstitutionReason.Allergy, "Milk", 1.0m, "Creamy texture, great for baking and coffee"),
                new("Milk", "Almond Milk", SubstitutionReason.Allergy, "Milk", 1.0m, "Lighter texture, slightly nutty flavor"),
                new("Milk", "Soy Milk", SubstitutionReason.Allergy, "Milk", 1.0m, "High protein, good for baking"),
                new("Milk", "Coconut Milk", SubstitutionReason.Allergy, "Milk", 1.0m, "Rich and creamy, adds coconut flavor"),
                new("Milk", "Rice Milk", SubstitutionReason.Allergy, "Milk", 1.0m, "Very light, slightly sweet — good for cereal"),
                new("Milk", "Hemp Milk", SubstitutionReason.Allergy, "Milk", 1.0m, "Earthy flavour, good source of omega-3"),
                new("Milk", "Pea Milk", SubstitutionReason.Allergy, "Milk", 1.0m, "High protein, neutral taste, froths well"),
                new("Milk", "Macadamia Milk", SubstitutionReason.Allergy, "Milk", 1.0m, "Rich and creamy, works well in coffee"),
                new("Milk", "Flax Milk", SubstitutionReason.Allergy, "Milk", 1.0m, "Mild flavour, good for smoothies"),

                // ========== DAIRY — BUTTER ==========
                new("Butter", "Coconut Oil", SubstitutionReason.Allergy, "Milk", 1.0m, "Solid at room temp, good for baking"),
                new("Butter", "Olive Oil", SubstitutionReason.Allergy, "Milk", 0.75m, "Use 3/4 the amount, best for savory dishes"),
                new("Butter", "Vegan Butter", SubstitutionReason.Allergy, "Milk", 1.0m, "Direct 1:1 replacement"),
                new("Butter", "Avocado", SubstitutionReason.Allergy, "Milk", 1.0m, "Great for baking, adds moisture"),
                new("Butter", "Ghee", SubstitutionReason.DietaryPreference, null, 1.0m, "Clarified butter, lactose-free"),
                new("Butter", "Applesauce", SubstitutionReason.Health, null, 0.75m, "Use 3/4 amount in baking, reduces fat significantly"),
                new("Butter", "Mashed Avocado", SubstitutionReason.Health, null, 1.0m, "Replaces butter in baking for healthy fats"),
                new("Butter", "Nut Butter", SubstitutionReason.Taste, null, 1.0m, "Works in cookies and brownies, adds nutty depth"),

                // ========== DAIRY — CREAM ==========
                new("Heavy Cream", "Coconut Cream", SubstitutionReason.Allergy, "Milk", 1.0m, "Thick and rich, slight coconut taste"),
                new("Heavy Cream", "Cashew Cream", SubstitutionReason.Allergy, "Milk", 1.0m, "Blend soaked cashews with water"),
                new("Heavy Cream", "Silken Tofu Cream", SubstitutionReason.Allergy, "Milk", 1.0m, "Blend silken tofu until smooth"),
                new("Heavy Cream", "Oat Cream", SubstitutionReason.Allergy, "Milk", 1.0m, "Commercial oat-based cream alternative"),
                new("Sour Cream", "Greek Yogurt", SubstitutionReason.DietaryPreference, null, 1.0m, "Similar tang, more protein"),
                new("Sour Cream", "Coconut Yogurt", SubstitutionReason.Allergy, "Milk", 1.0m, "Dairy-free alternative"),
                new("Sour Cream", "Cashew Sour Cream", SubstitutionReason.Allergy, "Milk", 1.0m, "Blend soaked cashews with lemon and apple cider vinegar"),
                new("Whipping Cream", "Aquafaba", SubstitutionReason.Allergy, "Milk", 1.0m, "Whip chickpea liquid to stiff peaks — works as dairy-free whipped cream"),
                new("Whipping Cream", "Chilled Coconut Cream", SubstitutionReason.Allergy, "Milk", 1.0m, "Chill overnight, whip cold for best results"),
                new("Buttermilk", "Oat Milk with Vinegar", SubstitutionReason.Allergy, "Milk", 1.0m, "1 cup oat milk + 1 tbsp vinegar, let sit 5 min"),
                new("Buttermilk", "Soy Milk with Lemon", SubstitutionReason.Allergy, "Milk", 1.0m, "1 cup soy milk + 1 tbsp lemon juice"),
                new("Buttermilk", "Plain Yogurt", SubstitutionReason.Availability, null, 1.0m, "Thin with a splash of water if needed"),
                new("Condensed Milk", "Coconut Condensed Milk", SubstitutionReason.Allergy, "Milk", 1.0m, "Direct replacement in dessert recipes"),

                // ========== DAIRY — CHEESE ==========
                new("Parmesan", "Nutritional Yeast", SubstitutionReason.Allergy, "Milk", 0.5m, "Use half the amount, cheesy umami flavor"),
                new("Cream Cheese", "Cashew Cream Cheese", SubstitutionReason.Allergy, "Milk", 1.0m, "Blend soaked cashews with lemon"),
                new("Ricotta", "Tofu Ricotta", SubstitutionReason.Allergy, "Milk", 1.0m, "Blend firm tofu with lemon and salt"),
                new("Mozzarella", "Vegan Mozzarella", SubstitutionReason.Allergy, "Milk", 1.0m, "Melts similarly, good on pizza"),
                new("Cheddar", "Vegan Cheddar", SubstitutionReason.Allergy, "Milk", 1.0m, "Sharp cashew-based alternatives work well"),
                new("Feta", "Firm Tofu with Brine", SubstitutionReason.Allergy, "Milk", 1.0m, "Crumble firm tofu, marinate in salt, lemon, olive oil"),
                new("Brie", "Cashew Brie", SubstitutionReason.Allergy, "Milk", 1.0m, "Fermented cashew-based soft cheese"),
                new("Halloumi", "Firm Tofu", SubstitutionReason.Allergy, "Milk", 1.0m, "Press and grill — holds shape well"),
                new("Halloumi", "Paneer", SubstitutionReason.Availability, null, 1.0m, "Grills well without melting"),
                new("Paneer", "Firm Tofu", SubstitutionReason.Allergy, "Milk", 1.0m, "Press extra firm tofu, use same way"),
                new("Blue Cheese", "Cashew Blue Cheese", SubstitutionReason.Allergy, "Milk", 1.0m, "Add spirulina for color, blue cheese cultures for tang"),
                new("Cottage Cheese", "Silken Tofu", SubstitutionReason.Allergy, "Milk", 1.0m, "Crumble for similar texture"),

                // ========== EGGS ==========
                new("Egg", "Flax Egg", SubstitutionReason.Allergy, "Eggs", 1.0m, "1 tbsp ground flax + 3 tbsp water per egg"),
                new("Egg", "Chia Egg", SubstitutionReason.Allergy, "Eggs", 1.0m, "1 tbsp chia seeds + 3 tbsp water per egg"),
                new("Egg", "Applesauce", SubstitutionReason.Allergy, "Eggs", 0.25m, "1/4 cup per egg, adds sweetness"),
                new("Egg", "Mashed Banana", SubstitutionReason.Allergy, "Eggs", 0.25m, "1/4 cup per egg, adds banana flavor"),
                new("Egg", "Silken Tofu", SubstitutionReason.Allergy, "Eggs", 0.25m, "1/4 cup blended per egg"),
                new("Egg", "Aquafaba", SubstitutionReason.Allergy, "Eggs", 3.0m, "3 tbsp chickpea water per egg"),
                new("Egg", "Commercial Egg Replacer", SubstitutionReason.Allergy, "Eggs", 1.0m, "Follow package instructions"),
                new("Egg", "Pumpkin Puree", SubstitutionReason.Allergy, "Eggs", 0.25m, "1/4 cup per egg, works great in dense bakes"),
                new("Egg", "Yogurt", SubstitutionReason.Allergy, "Eggs", 0.25m, "1/4 cup per egg, adds moisture and binding"),
                new("Egg Yolk", "Cashew Paste", SubstitutionReason.Allergy, "Eggs", 1.0m, "For richness in sauces and creams"),
                new("Egg White", "Aquafaba", SubstitutionReason.Allergy, "Eggs", 1.0m, "3 tbsp per egg white — whips to stiff peaks"),

                // ========== GLUTEN — FLOUR ==========
                new("All-Purpose Flour", "Almond Flour", SubstitutionReason.Allergy, "Gluten", 1.0m, "May need binding agent, denser result"),
                new("All-Purpose Flour", "Oat Flour", SubstitutionReason.Allergy, "Gluten", 1.0m, "Use certified gluten-free oats"),
                new("All-Purpose Flour", "Rice Flour", SubstitutionReason.Allergy, "Gluten", 1.0m, "Lighter texture, blend with other GF flours"),
                new("All-Purpose Flour", "Coconut Flour", SubstitutionReason.Allergy, "Gluten", 0.25m, "Use 1/4 amount, very absorbent"),
                new("All-Purpose Flour", "Gluten-Free Flour Blend", SubstitutionReason.Allergy, "Gluten", 1.0m, "Best 1:1 replacement"),
                new("All-Purpose Flour", "Cassava Flour", SubstitutionReason.Allergy, "Gluten", 1.0m, "Close texture to wheat flour, good for tortillas"),
                new("All-Purpose Flour", "Chickpea Flour", SubstitutionReason.Allergy, "Gluten", 1.0m, "High protein, good for savoury dishes and flatbreads"),
                new("All-Purpose Flour", "Buckwheat Flour", SubstitutionReason.Allergy, "Gluten", 1.0m, "Earthy flavour, great for pancakes and noodles"),
                new("All-Purpose Flour", "Teff Flour", SubstitutionReason.Allergy, "Gluten", 1.0m, "Nutty, slightly sweet — traditional in injera"),
                new("Bread Flour", "Gluten-Free Bread Flour", SubstitutionReason.Allergy, "Gluten", 1.0m, "Add xanthan gum if not included"),
                new("Self-Raising Flour", "GF Self-Raising Flour", SubstitutionReason.Allergy, "Gluten", 1.0m, "Direct replacement in cakes and scones"),
                new("Cornstarch", "Arrowroot Powder", SubstitutionReason.Availability, null, 1.0m, "Works the same, slightly clearer finish"),
                new("Cornstarch", "Tapioca Starch", SubstitutionReason.Availability, null, 1.0m, "Good for thickening sauces"),
                new("Cornstarch", "Potato Starch", SubstitutionReason.Availability, null, 1.0m, "1:1 swap, slightly heavier"),

                // ========== GLUTEN — BREAD & PASTA ==========
                new("Bread Crumbs", "Gluten-Free Bread Crumbs", SubstitutionReason.Allergy, "Gluten", 1.0m, "Direct replacement"),
                new("Bread Crumbs", "Crushed Rice Crackers", SubstitutionReason.Allergy, "Gluten", 1.0m, "Pulse in food processor"),
                new("Bread Crumbs", "Almond Meal", SubstitutionReason.Allergy, "Gluten", 1.0m, "Adds nutty flavor"),
                new("Bread Crumbs", "Crushed Cornflakes", SubstitutionReason.Allergy, "Gluten", 1.0m, "Use GF cornflakes — great crispy coating"),
                new("Bread Crumbs", "Rolled Oats", SubstitutionReason.Allergy, "Gluten", 1.0m, "Use certified GF oats, pulse lightly"),
                new("Pasta", "Rice Noodles", SubstitutionReason.Allergy, "Gluten", 1.0m, "Cook according to package"),
                new("Pasta", "Zucchini Noodles", SubstitutionReason.Allergy, "Gluten", 1.0m, "Low carb option, spiralize fresh"),
                new("Pasta", "Gluten-Free Pasta", SubstitutionReason.Allergy, "Gluten", 1.0m, "Direct replacement"),
                new("Pasta", "Chickpea Pasta", SubstitutionReason.Allergy, "Gluten", 1.0m, "Higher protein, slightly dense"),
                new("Pasta", "Lentil Pasta", SubstitutionReason.Allergy, "Gluten", 1.0m, "High protein and fibre"),
                new("Pasta", "Soba Noodles", SubstitutionReason.Allergy, "Gluten", 1.0m, "Use 100% buckwheat for GF, nutty flavour"),
                new("Lasagne Sheets", "GF Lasagne Sheets", SubstitutionReason.Allergy, "Gluten", 1.0m, "Available in most supermarkets"),
                new("Lasagne Sheets", "Thinly Sliced Zucchini", SubstitutionReason.Allergy, "Gluten", 1.0m, "Layer raw, moisture releases during baking"),
                new("Soy Sauce", "Tamari", SubstitutionReason.Allergy, "Gluten", 1.0m, "Gluten-free soy sauce alternative"),
                new("Soy Sauce", "Coconut Aminos", SubstitutionReason.Allergy, "Gluten", 1.0m, "Soy and gluten free, slightly sweeter"),
                new("Couscous", "Quinoa", SubstitutionReason.Allergy, "Gluten", 1.0m, "Higher protein, fluffier — cook the same way"),
                new("Couscous", "Cauliflower Couscous", SubstitutionReason.Allergy, "Gluten", 1.0m, "Pulse cauliflower until grain-sized"),
                new("Barley", "Buckwheat Groats", SubstitutionReason.Allergy, "Gluten", 1.0m, "Similar chewy texture when cooked"),

                // ========== NUTS ==========
                new("Peanut Butter", "Sunflower Seed Butter", SubstitutionReason.Allergy, "Peanuts", 1.0m, "Nut-free, similar texture"),
                new("Peanut Butter", "Tahini", SubstitutionReason.Allergy, "Peanuts", 1.0m, "Sesame-based, slightly bitter"),
                new("Peanut Butter", "Soy Nut Butter", SubstitutionReason.Allergy, "Peanuts", 1.0m, "Made from roasted soybeans"),
                new("Peanut Butter", "Pumpkin Seed Butter", SubstitutionReason.Allergy, "Peanuts", 1.0m, "Nut and peanut free, earthy flavour"),
                new("Peanuts", "Roasted Chickpeas", SubstitutionReason.Allergy, "Peanuts", 1.0m, "Similar crunch for snacking and salads"),
                new("Peanuts", "Sunflower Seeds", SubstitutionReason.Allergy, "Peanuts", 1.0m, "Good in stir-fries and salads"),
                new("Almonds", "Sunflower Seeds", SubstitutionReason.Allergy, "Tree Nuts", 1.0m, "Similar crunch"),
                new("Almonds", "Pumpkin Seeds", SubstitutionReason.Allergy, "Tree Nuts", 1.0m, "Pepitas work well"),
                new("Almonds", "Hemp Seeds", SubstitutionReason.Allergy, "Tree Nuts", 1.0m, "Soft texture, nutty flavour, high protein"),
                new("Walnuts", "Sunflower Seeds", SubstitutionReason.Allergy, "Tree Nuts", 1.0m, "For baking and salads"),
                new("Walnuts", "Pumpkin Seeds", SubstitutionReason.Allergy, "Tree Nuts", 1.0m, "Slightly different flavour, works well in baking"),
                new("Cashews", "Sunflower Seeds", SubstitutionReason.Allergy, "Tree Nuts", 1.0m, "For savory dishes and stir-fries"),
                new("Pine Nuts", "Sunflower Seeds", SubstitutionReason.Allergy, "Tree Nuts", 1.0m, "Good for pesto"),
                new("Pine Nuts", "Hemp Seeds", SubstitutionReason.Allergy, "Tree Nuts", 1.0m, "Soft and nutty, works in pesto and salads"),
                new("Almond Milk", "Oat Milk", SubstitutionReason.Allergy, "Tree Nuts", 1.0m, "Nut-free milk alternative"),
                new("Almond Milk", "Rice Milk", SubstitutionReason.Allergy, "Tree Nuts", 1.0m, "Lighter, nut-free"),
                new("Almond Milk", "Pea Milk", SubstitutionReason.Allergy, "Tree Nuts", 1.0m, "High protein, neutral taste"),
                new("Almond Flour", "Sunflower Seed Flour", SubstitutionReason.Allergy, "Tree Nuts", 1.0m, "May turn green from chlorophyll"),
                new("Almond Flour", "Oat Flour", SubstitutionReason.Allergy, "Tree Nuts", 1.0m, "Lighter texture, use GF certified"),
                new("Tahini", "Sunflower Seed Butter", SubstitutionReason.Allergy, "Peanuts", 1.0m, "Nut-free alternative with similar consistency"),

                // ========== SOY ==========
                new("Tofu", "Chickpeas", SubstitutionReason.Allergy, "Soy", 1.0m, "Mash for similar texture"),
                new("Tofu", "Tempeh", SubstitutionReason.DietaryPreference, null, 1.0m, "Fermented, nuttier flavor"),
                new("Tofu", "Halloumi", SubstitutionReason.Allergy, "Soy", 1.0m, "Grills well, saltier — rinse before using"),
                new("Tofu", "Paneer", SubstitutionReason.Allergy, "Soy", 1.0m, "Firm Indian cheese, grills without melting"),
                new("Tofu", "Cauliflower Steaks", SubstitutionReason.Allergy, "Soy", 1.0m, "Thick slices roasted or grilled"),
                new("Soy Milk", "Oat Milk", SubstitutionReason.Allergy, "Soy", 1.0m, "Creamy, soy-free"),
                new("Soy Milk", "Coconut Milk", SubstitutionReason.Allergy, "Soy", 1.0m, "Rich and creamy"),
                new("Soy Milk", "Pea Milk", SubstitutionReason.Allergy, "Soy", 1.0m, "High protein, soy-free"),
                new("Soy Sauce", "Coconut Aminos", SubstitutionReason.Allergy, "Soy", 1.0m, "Soy-free, slightly sweeter"),
                new("Edamame", "Lima Beans", SubstitutionReason.Allergy, "Soy", 1.0m, "Similar texture"),
                new("Edamame", "Peas", SubstitutionReason.Allergy, "Soy", 1.0m, "Slightly sweeter, works in salads and sides"),
                new("Miso", "Chickpea Miso", SubstitutionReason.Allergy, "Soy", 1.0m, "Soy-free fermented paste"),
                new("Miso", "Tahini with Salt", SubstitutionReason.Allergy, "Soy", 0.5m, "Use half the amount, adds depth and salinity"),
                new("Tempeh", "Jackfruit", SubstitutionReason.Allergy, "Soy", 1.0m, "Shred young jackfruit for pulled texture"),

                // ========== FISH & SHELLFISH ==========
                new("Fish Sauce", "Soy Sauce", SubstitutionReason.Allergy, "Fish", 1.0m, "Less funky, add a pinch of salt"),
                new("Fish Sauce", "Coconut Aminos", SubstitutionReason.Allergy, "Fish", 1.5m, "Use a bit more, sweeter"),
                new("Fish Sauce", "Seaweed Broth with Soy Sauce", SubstitutionReason.Allergy, "Fish", 1.0m, "Simmer kombu in water, mix with soy sauce for oceanic umami"),
                new("Anchovy Paste", "Capers", SubstitutionReason.Allergy, "Fish", 1.0m, "For umami in Caesar dressing"),
                new("Anchovy Paste", "Miso Paste", SubstitutionReason.Allergy, "Fish", 0.5m, "Use half, adds salty umami depth"),
                new("Oyster Sauce", "Mushroom Sauce", SubstitutionReason.Allergy, "Shellfish", 1.0m, "Vegetarian oyster sauce"),
                new("Oyster Sauce", "Hoisin Sauce", SubstitutionReason.Allergy, "Shellfish", 1.0m, "Sweeter, slightly different but works in stir-fries"),
                new("Shrimp", "Hearts of Palm", SubstitutionReason.Allergy, "Shellfish", 1.0m, "Similar texture when chopped"),
                new("Shrimp", "King Oyster Mushrooms", SubstitutionReason.Allergy, "Shellfish", 1.0m, "Slice and sear — meaty bite, works in pasta"),
                new("Tuna", "Canned Chickpeas", SubstitutionReason.Allergy, "Fish", 1.0m, "Mash with mayo and seasoning for tuna salad alternative"),
                new("Tuna", "Jackfruit", SubstitutionReason.Allergy, "Fish", 1.0m, "Shred young jackfruit — great for tuna-style sandwiches"),
                new("Salmon", "Roasted Red Pepper", SubstitutionReason.Allergy, "Fish", 1.0m, "For colour and sweetness in salads and pasta"),
                new("Crab", "Hearts of Palm", SubstitutionReason.Allergy, "Shellfish", 1.0m, "Shred for crab cake alternative"),
                new("Sardines", "Olives", SubstitutionReason.Allergy, "Fish", 1.0m, "Salty, briny punch in pasta and salads"),
                new("Worcestershire Sauce", "Vegan Worcestershire", SubstitutionReason.Allergy, "Fish", 1.0m, "Anchovy-free version, same savoury depth"),

                // ========== MEAT — POULTRY ==========
                new("Chicken", "Tofu", SubstitutionReason.DietaryPreference, null, 1.0m, "Press firm tofu and cube or slice before cooking"),
                new("Chicken", "Chickpeas", SubstitutionReason.DietaryPreference, null, 1.0m, "Great in curries and stews"),
                new("Chicken", "Cauliflower", SubstitutionReason.DietaryPreference, null, 1.0m, "Roast first for best texture"),
                new("Chicken", "Turkey", SubstitutionReason.DietaryPreference, null, 1.0m, "Direct 1:1 replacement in most recipes"),
                new("Chicken", "Lamb", SubstitutionReason.DietaryPreference, null, 1.0m, "Richer flavour, adjust spices accordingly"),
                new("Chicken", "Jackfruit", SubstitutionReason.DietaryPreference, null, 1.0m, "Young green jackfruit mimics pulled chicken"),
                new("Chicken", "Seitan", SubstitutionReason.DietaryPreference, null, 1.0m, "Wheat-based, very meaty texture"),
                new("Chicken Breast", "Turkey Breast", SubstitutionReason.Availability, null, 1.0m, "Leaner, very similar in most dishes"),
                new("Chicken Thighs", "Duck Legs", SubstitutionReason.Taste, null, 1.0m, "Richer, fattier — reduce added oil"),
                new("Duck", "Chicken Thighs", SubstitutionReason.Availability, null, 1.0m, "Less rich, use skin-on for best result"),
                new("Duck", "Lamb Shoulder", SubstitutionReason.Taste, null, 1.0m, "Strong flavour, slow cook similarly"),
                new("Turkey", "Chicken", SubstitutionReason.Availability, null, 1.0m, "Reduce cooking time slightly"),
                new("Turkey Mince", "Chicken Mince", SubstitutionReason.Availability, null, 1.0m, "Direct 1:1 swap"),

                // ========== MEAT — BEEF & RED MEAT ==========
                new("Beef", "Lamb", SubstitutionReason.DietaryPreference, null, 1.0m, "Similar cooking times, richer flavour"),
                new("Beef", "Chicken", SubstitutionReason.DietaryPreference, null, 1.0m, "Leaner, reduce cooking time"),
                new("Beef", "Lentils", SubstitutionReason.DietaryPreference, null, 1.0m, "Great in bolognese and stews"),
                new("Beef", "Mushrooms", SubstitutionReason.DietaryPreference, null, 1.0m, "Use portobello or mixed mushrooms for umami"),
                new("Beef", "Jackfruit", SubstitutionReason.DietaryPreference, null, 1.0m, "For pulled or shredded beef dishes"),
                new("Beef", "Seitan", SubstitutionReason.DietaryPreference, null, 1.0m, "Very meaty wheat-based protein"),
                new("Ground Beef", "Ground Lamb", SubstitutionReason.DietaryPreference, null, 1.0m, "Works well in burgers and kebabs"),
                new("Ground Beef", "Ground Turkey", SubstitutionReason.DietaryPreference, null, 1.0m, "Leaner option, similar texture"),
                new("Ground Beef", "Lentils", SubstitutionReason.DietaryPreference, null, 1.0m, "Cook until soft, season well"),
                new("Ground Beef", "Walnut Meat", SubstitutionReason.DietaryPreference, null, 1.0m, "Pulse walnuts with spices — works great in tacos"),
                new("Steak", "Portobello Mushroom", SubstitutionReason.DietaryPreference, null, 1.0m, "Marinate and grill whole cap"),
                new("Steak", "Cauliflower Steak", SubstitutionReason.DietaryPreference, null, 1.0m, "Thick-cut, roast or grill with bold seasoning"),
                new("Lamb", "Beef", SubstitutionReason.Availability, null, 1.0m, "Milder flavour, works in most lamb dishes"),
                new("Lamb", "Goat Meat", SubstitutionReason.Availability, null, 1.0m, "Similar taste, slightly leaner"),
                new("Pork", "Chicken", SubstitutionReason.Religious, null, 1.0m, "Halal-friendly swap in most recipes"),
                new("Pork", "Turkey", SubstitutionReason.Religious, null, 1.0m, "Works well in sausages and mince dishes"),
                new("Pork", "Lamb", SubstitutionReason.Religious, null, 1.0m, "Richer flavour, good in slow-cooked dishes"),
                new("Bacon", "Turkey Bacon", SubstitutionReason.Religious, null, 1.0m, "Lower fat, halal-friendly"),
                new("Bacon", "Tempeh Bacon", SubstitutionReason.DietaryPreference, null, 1.0m, "Marinate and fry tempeh strips"),
                new("Bacon", "Coconut Bacon", SubstitutionReason.DietaryPreference, null, 1.0m, "Toasted coconut flakes with smoked paprika and soy sauce"),
                new("Ham", "Smoked Turkey Slices", SubstitutionReason.Religious, null, 1.0m, "Works great in sandwiches and pasta"),
                new("Prosciutto", "Smoked Salmon", SubstitutionReason.Religious, null, 1.0m, "Salty and silky, good wrapped around melon"),
                new("Chorizo", "Smoked Paprika Chickpeas", SubstitutionReason.DietaryPreference, null, 1.0m, "Fry chickpeas with smoked paprika and olive oil"),
                new("Chorizo", "Halal Beef Sausage", SubstitutionReason.Religious, null, 1.0m, "Season with paprika and garlic to mimic chorizo"),
                new("Pepperoni", "Vegan Pepperoni", SubstitutionReason.DietaryPreference, null, 1.0m, "Available in most supermarkets"),
                new("Pepperoni", "Sliced Halal Salami", SubstitutionReason.Religious, null, 1.0m, "Direct replacement on pizza"),
                new("Sausage", "Chicken Sausage", SubstitutionReason.Religious, null, 1.0m, "Lighter, halal-friendly"),
                new("Sausage", "Lentil Sausage", SubstitutionReason.DietaryPreference, null, 1.0m, "Plant-based, available in most stores"),

                // ========== AROMATICS ==========
                new("Garlic", "Garlic Powder", SubstitutionReason.Availability, null, 0.125m, "1/8 tsp powder per fresh clove"),
                new("Garlic", "Shallots", SubstitutionReason.Taste, null, 1.0m, "Milder flavour, use same amount"),
                new("Garlic", "Asafoetida", SubstitutionReason.Taste, null, 0.1m, "Very pungent — use a tiny pinch"),
                new("Garlic", "Green Onions", SubstitutionReason.Availability, null, 1.0m, "Use the white parts, milder"),
                new("Ginger", "Ground Ginger", SubstitutionReason.Availability, null, 0.25m, "1/4 tsp ground per 1 tsp fresh"),
                new("Ginger", "Galangal", SubstitutionReason.Availability, null, 1.0m, "Similar but sharper, use same amount"),
                new("Ginger", "Turmeric", SubstitutionReason.Availability, null, 0.5m, "Less spicy, more earthy — good in curries"),
                new("Onion", "Shallots", SubstitutionReason.Taste, null, 1.0m, "Sweeter, milder flavour"),
                new("Onion", "Leeks", SubstitutionReason.Taste, null, 1.0m, "Use white and light green parts only"),
                new("Onion", "Onion Powder", SubstitutionReason.Availability, null, 0.25m, "1/4 tsp powder per small onion"),
                new("Onion", "Fennel Bulb", SubstitutionReason.Taste, null, 1.0m, "Anise notes, softens when cooked"),
                new("Shallots", "Red Onion", SubstitutionReason.Availability, null, 1.0m, "Slightly stronger, dice finely"),
                new("Spring Onions", "Chives", SubstitutionReason.Availability, null, 1.0m, "Milder, works raw"),
                new("Celery", "Fennel Stalks", SubstitutionReason.Availability, null, 1.0m, "Similar crunch, anise flavour"),
                new("Celery", "Bok Choy Stalks", SubstitutionReason.Availability, null, 1.0m, "Milder, works in stir-fries and soups"),

                // ========== ACIDS & CITRUS ==========
                new("Lemon Juice", "Lime Juice", SubstitutionReason.Availability, null, 1.0m, "Slightly more tart, works in most recipes"),
                new("Lemon Juice", "Apple Cider Vinegar", SubstitutionReason.Availability, null, 0.5m, "Use half the amount, more pungent"),
                new("Lemon Juice", "White Wine Vinegar", SubstitutionReason.Availability, null, 0.5m, "Mild acidity, good for dressings"),
                new("Lemon Juice", "Orange Juice", SubstitutionReason.Taste, null, 1.0m, "Sweeter and less tart, good in marinades"),
                new("Lemon Zest", "Orange Zest", SubstitutionReason.Availability, null, 1.0m, "Sweeter citrus note"),
                new("Lime Juice", "Lemon Juice", SubstitutionReason.Availability, null, 1.0m, "Works in most recipes"),
                new("Lime Juice", "White Wine Vinegar", SubstitutionReason.Availability, null, 0.5m, "Use half, works in dressings and marinades"),
                new("Orange Juice", "Pineapple Juice", SubstitutionReason.Availability, null, 1.0m, "Sweeter, tropical twist"),
                new("Balsamic Vinegar", "Red Wine Vinegar with Honey", SubstitutionReason.Availability, null, 1.0m, "Mix equal parts for similar sweet-tart depth"),
                new("Red Wine Vinegar", "Apple Cider Vinegar", SubstitutionReason.Availability, null, 1.0m, "Slightly fruitier, works in dressings"),
                new("White Wine Vinegar", "Rice Vinegar", SubstitutionReason.Availability, null, 1.0m, "Milder, works in Asian dressings"),
                new("Tamarind Paste", "Lime Juice with Brown Sugar", SubstitutionReason.Availability, null, 1.0m, "Mix for similar sour-sweet tang"),

                // ========== OILS & FATS ==========
                new("Olive Oil", "Avocado Oil", SubstitutionReason.DietaryPreference, null, 1.0m, "Higher smoke point, neutral flavour"),
                new("Olive Oil", "Coconut Oil", SubstitutionReason.DietaryPreference, null, 1.0m, "Solid at room temp, adds coconut notes"),
                new("Olive Oil", "Vegetable Oil", SubstitutionReason.Availability, null, 1.0m, "Neutral flavour, direct replacement"),
                new("Olive Oil", "Sunflower Oil", SubstitutionReason.Availability, null, 1.0m, "Neutral, good for frying"),
                new("Olive Oil", "Grapeseed Oil", SubstitutionReason.Availability, null, 1.0m, "Light, high smoke point"),
                new("Coconut Oil", "Butter", SubstitutionReason.Availability, null, 1.0m, "Richer flavour, adds dairy"),
                new("Coconut Oil", "Avocado Oil", SubstitutionReason.Health, null, 1.0m, "Liquid at room temp, neutral flavour"),
                new("Ghee", "Butter", SubstitutionReason.Availability, null, 1.0m, "Contains lactose unlike ghee"),
                new("Ghee", "Coconut Oil", SubstitutionReason.DietaryPreference, null, 1.0m, "Dairy-free, high smoke point"),
                new("Lard", "Vegetable Shortening", SubstitutionReason.Religious, null, 1.0m, "Plant-based fat"),
                new("Lard", "Coconut Oil", SubstitutionReason.Religious, null, 1.0m, "Solid at room temp"),
                new("Sesame Oil", "Toasted Sunflower Oil", SubstitutionReason.Availability, null, 1.0m, "Less aromatic, add a drop of tahini if possible"),

                // ========== VEGETABLES ==========
                new("Spinach", "Kale", SubstitutionReason.Availability, null, 1.0m, "Heartier, blanch or massage before using raw"),
                new("Spinach", "Swiss Chard", SubstitutionReason.Availability, null, 1.0m, "Similar nutrition, slightly stronger flavour"),
                new("Spinach", "Arugula", SubstitutionReason.Taste, null, 1.0m, "Peppery kick, works raw in salads"),
                new("Kale", "Spinach", SubstitutionReason.Availability, null, 1.0m, "Softer, use raw without massaging"),
                new("Kale", "Collard Greens", SubstitutionReason.Availability, null, 1.0m, "Tougher, needs longer cooking"),
                new("Zucchini", "Yellow Squash", SubstitutionReason.Availability, null, 1.0m, "Similar texture and flavour"),
                new("Zucchini", "Cucumber", SubstitutionReason.Availability, null, 1.0m, "Use raw in salads, not suitable for cooking"),
                new("Zucchini", "Eggplant", SubstitutionReason.Availability, null, 1.0m, "Cook similarly, slightly denser"),
                new("Eggplant", "Portobello Mushrooms", SubstitutionReason.Availability, null, 1.0m, "Meaty texture, soak up sauces well"),
                new("Eggplant", "Zucchini", SubstitutionReason.Availability, null, 1.0m, "Lighter, less absorbent"),
                new("Bell Pepper", "Poblano Pepper", SubstitutionReason.Availability, null, 1.0m, "Mildly spicy, good in roasting"),
                new("Bell Pepper", "Zucchini", SubstitutionReason.Availability, null, 1.0m, "Different flavour but similar bulk in dishes"),
                new("Tomatoes", "Canned Tomatoes", SubstitutionReason.Availability, null, 1.0m, "Use drained for fresh tomato texture"),
                new("Tomatoes", "Roasted Red Peppers", SubstitutionReason.Taste, null, 1.0m, "Sweeter, great in pasta sauces"),
                new("Cauliflower", "Broccoli", SubstitutionReason.Availability, null, 1.0m, "Slightly stronger flavour, similar texture when roasted"),
                new("Broccoli", "Cauliflower", SubstitutionReason.Availability, null, 1.0m, "Milder, works in most cooked dishes"),
                new("Sweet Potato", "Butternut Squash", SubstitutionReason.Availability, null, 1.0m, "Similar sweetness, slightly less dense"),
                new("Sweet Potato", "Carrot", SubstitutionReason.Availability, null, 1.0m, "Less sweet, works in soups and roasting"),
                new("Potato", "Sweet Potato", SubstitutionReason.Health, null, 1.0m, "Higher fibre and vitamins, slightly sweeter"),
                new("Potato", "Cauliflower", SubstitutionReason.Health, null, 1.0m, "Mash or roast, much lower carb"),
                new("Potato", "Turnip", SubstitutionReason.Availability, null, 1.0m, "Lower carb, slightly bitter — good in stews"),
                new("Mushrooms", "Eggplant", SubstitutionReason.Availability, null, 1.0m, "Dense and meaty when cooked"),
                new("Mushrooms", "Zucchini", SubstitutionReason.Availability, null, 1.0m, "Lighter option in pasta and stir-fries"),
                new("Corn", "Frozen Peas", SubstitutionReason.Availability, null, 1.0m, "Sweeter, different texture but works in salads"),
                new("Corn", "Diced Bell Pepper", SubstitutionReason.Availability, null, 1.0m, "Adds crunch and colour"),
                new("Cucumber", "Zucchini", SubstitutionReason.Availability, null, 1.0m, "Use raw only, similar crunch"),
                new("Fennel", "Celery", SubstitutionReason.Availability, null, 1.0m, "Milder, no anise notes"),
                new("Asparagus", "Green Beans", SubstitutionReason.Availability, null, 1.0m, "Similar texture when blanched or roasted"),
                new("Asparagus", "Broccolini", SubstitutionReason.Availability, null, 1.0m, "Tender stalks, works the same"),
                new("Green Beans", "Sugar Snap Peas", SubstitutionReason.Availability, null, 1.0m, "Sweeter, works raw or cooked"),
                new("Leeks", "Onion", SubstitutionReason.Availability, null, 1.0m, "Stronger flavour, dice finely"),
                new("Bok Choy", "Napa Cabbage", SubstitutionReason.Availability, null, 1.0m, "Similar mild flavour, works in stir-fries"),
                new("Cabbage", "Brussels Sprouts", SubstitutionReason.Taste, null, 1.0m, "Stronger flavour, shred finely"),
                new("Artichoke Hearts", "Hearts of Palm", SubstitutionReason.Availability, null, 1.0m, "Similar tender texture in salads and dips"),

                // ========== TOMATO PRODUCTS ==========
                new("Tomato Paste", "Tomato Puree", SubstitutionReason.Availability, null, 2.0m, "Use double the amount"),
                new("Tomato Puree", "Blended Canned Tomatoes", SubstitutionReason.Availability, null, 1.0m, "Blend and strain for smooth result"),
                new("Tomato Sauce", "Blended Canned Tomatoes with Seasoning", SubstitutionReason.Availability, null, 1.0m, "Add garlic, salt and olive oil"),
                new("Sun-Dried Tomatoes", "Roasted Red Peppers", SubstitutionReason.Availability, null, 1.0m, "Less intense, sweeter flavour"),
                new("Passata", "Blended Chopped Tomatoes", SubstitutionReason.Availability, null, 1.0m, "Blend until smooth, strain if needed"),

                // ========== GRAINS & RICE ==========
                new("Rice", "Quinoa", SubstitutionReason.Health, null, 1.0m, "Higher protein, nuttier flavour"),
                new("Rice", "Cauliflower Rice", SubstitutionReason.Health, null, 1.0m, "Low carb, pulse cauliflower in food processor"),
                new("Rice", "Bulgur Wheat", SubstitutionReason.DietaryPreference, null, 1.0m, "Nuttier flavour, slightly chewy"),
                new("Rice", "Farro", SubstitutionReason.DietaryPreference, null, 1.0m, "Chewy, nutty — great in grain bowls"),
                new("Rice", "Millet", SubstitutionReason.Health, null, 1.0m, "Gluten-free grain, slightly sweet"),
                new("Rice", "Freekeh", SubstitutionReason.Taste, null, 1.0m, "Smoky, nutty — great in pilafs"),
                new("Basmati Rice", "Long-Grain Rice", SubstitutionReason.Availability, null, 1.0m, "Similar texture, less aromatic"),
                new("Basmati Rice", "Jasmine Rice", SubstitutionReason.Availability, null, 1.0m, "Slightly stickier, fragrant"),
                new("Arborio Rice", "Short-Grain Brown Rice", SubstitutionReason.Health, null, 1.0m, "Less creamy, add extra stock for risotto"),
                new("Arborio Rice", "Pearl Barley", SubstitutionReason.Taste, null, 1.0m, "Creamy when cooked, nutty flavour"),
                new("Couscous", "Quinoa", SubstitutionReason.Allergy, "Gluten", 1.0m, "Higher protein, fluffier — cook the same way"),
                new("Couscous", "Millet", SubstitutionReason.Allergy, "Gluten", 1.0m, "Gluten-free, similar quick cook time"),
                new("Oats", "Buckwheat Groats", SubstitutionReason.Allergy, "Gluten", 1.0m, "Gluten-free, similar porridge result"),
                new("Oats", "Quinoa Flakes", SubstitutionReason.Allergy, "Gluten", 1.0m, "Good for GF porridge and granola"),
                new("Breadcrumbs", "Panko", SubstitutionReason.Availability, null, 1.0m, "Crispier, lighter texture"),
                new("Polenta", "Grits", SubstitutionReason.Availability, null, 1.0m, "Similar corn-based, slightly coarser"),
                new("Semolina", "Fine Cornmeal", SubstitutionReason.Allergy, "Gluten", 1.0m, "Works in porridge and some baking"),
                new("Barley", "Farro", SubstitutionReason.Availability, null, 1.0m, "Similar chewy texture and cooking time"),
                new("Quinoa", "Millet", SubstitutionReason.Availability, null, 1.0m, "Similar cooking time, slightly lighter"),

                // ========== HERBS ==========
                new("Fresh Cilantro", "Fresh Parsley", SubstitutionReason.Taste, null, 1.0m, "Milder flavour, no citrus notes"),
                new("Fresh Cilantro", "Thai Basil", SubstitutionReason.Taste, null, 1.0m, "Slightly different but works in Asian dishes"),
                new("Fresh Basil", "Fresh Mint", SubstitutionReason.Availability, null, 1.0m, "Cooler flavour, good in salads and drinks"),
                new("Fresh Basil", "Dried Basil", SubstitutionReason.Availability, null, 0.33m, "Use 1/3 the amount — dried is more concentrated"),
                new("Fresh Thyme", "Dried Thyme", SubstitutionReason.Availability, null, 0.33m, "1/3 the amount — stronger when dried"),
                new("Fresh Thyme", "Fresh Rosemary", SubstitutionReason.Taste, null, 0.5m, "More pungent, use half the amount"),
                new("Fresh Rosemary", "Fresh Thyme", SubstitutionReason.Availability, null, 1.0m, "Milder, works in roasted dishes"),
                new("Fresh Dill", "Fresh Tarragon", SubstitutionReason.Availability, null, 1.0m, "Anise notes, works in fish and egg dishes"),
                new("Fresh Mint", "Fresh Basil", SubstitutionReason.Availability, null, 1.0m, "Sweeter, works in salads and desserts"),
                new("Fresh Oregano", "Dried Oregano", SubstitutionReason.Availability, null, 0.33m, "Use 1/3 the amount of dried"),
                new("Fresh Parsley", "Fresh Chervil", SubstitutionReason.Availability, null, 1.0m, "Delicate anise flavour, works raw"),
                new("Lemongrass", "Lemon Zest with Ginger", SubstitutionReason.Availability, null, 1.0m, "Add lemon zest and a slice of ginger for similar aroma"),
                new("Bay Leaves", "Dried Thyme", SubstitutionReason.Availability, null, 0.5m, "Less aromatic, use half the amount"),

                // ========== SPICES ==========
                new("Cumin", "Caraway Seeds", SubstitutionReason.Availability, null, 1.0m, "Similar earthy flavour"),
                new("Cumin", "Coriander", SubstitutionReason.Availability, null, 1.0m, "Lighter, citrusy alternative"),
                new("Coriander", "Cumin", SubstitutionReason.Availability, null, 1.0m, "Earthier, use same amount"),
                new("Turmeric", "Saffron", SubstitutionReason.Availability, null, 0.25m, "For colour only, flavour differs"),
                new("Turmeric", "Annatto Powder", SubstitutionReason.Availability, null, 0.5m, "Deep golden-orange colour, mild earthy flavour"),
                new("Paprika", "Chili Powder", SubstitutionReason.Availability, null, 0.5m, "Hotter, use less"),
                new("Paprika", "Cayenne Pepper", SubstitutionReason.Availability, null, 0.25m, "Much hotter, use sparingly"),
                new("Smoked Paprika", "Chipotle Powder", SubstitutionReason.Availability, null, 0.5m, "Smokier and spicier, use less"),
                new("Chili Flakes", "Cayenne Pepper", SubstitutionReason.Availability, null, 0.5m, "Finer heat, use half the amount"),
                new("Nutmeg", "Mace", SubstitutionReason.Availability, null, 1.0m, "Outer shell of nutmeg, very similar flavour"),
                new("Nutmeg", "Allspice", SubstitutionReason.Availability, null, 0.5m, "Warm and spicy, use half"),
                new("Allspice", "Mixed Spice", SubstitutionReason.Availability, null, 1.0m, "Similar warm spice blend"),
                new("Cinnamon", "Mixed Spice", SubstitutionReason.Availability, null, 1.0m, "Slightly different, works in most baked goods"),
                new("Cinnamon", "Cardamom", SubstitutionReason.Taste, null, 0.5m, "More floral and intense, use less"),
                new("Cardamom", "Cinnamon", SubstitutionReason.Availability, null, 1.0m, "Less floral, milder"),
                new("Vanilla Extract", "Vanilla Bean Paste", SubstitutionReason.Availability, null, 1.0m, "Thicker, includes seeds — more intense"),
                new("Vanilla Extract", "Almond Extract", SubstitutionReason.Taste, null, 0.5m, "Stronger, use half — different but complementary"),
                new("Saffron", "Turmeric", SubstitutionReason.Cost, null, 0.25m, "For colour only — flavour is different but works"),
                new("Star Anise", "Fennel Seeds", SubstitutionReason.Availability, null, 1.0m, "Similar anise notes, milder"),
                new("Five Spice", "Star Anise with Cinnamon", SubstitutionReason.Availability, null, 1.0m, "Combine for a similar warm spice base"),

                // ========== CONDIMENTS ==========
                new("Mayonnaise", "Greek Yogurt", SubstitutionReason.Health, null, 1.0m, "Lower fat, tangy"),
                new("Mayonnaise", "Avocado", SubstitutionReason.DietaryPreference, null, 1.0m, "Mash for spreads"),
                new("Mayonnaise", "Vegan Mayo", SubstitutionReason.Allergy, "Eggs", 1.0m, "Egg-free alternative"),
                new("Mayonnaise", "Hummus", SubstitutionReason.Health, null, 1.0m, "Works as a spread or dip"),
                new("Ketchup", "Tomato Paste with Vinegar", SubstitutionReason.Health, null, 1.0m, "Add sugar and vinegar to tomato paste"),
                new("Mustard", "Horseradish", SubstitutionReason.Taste, null, 0.5m, "Spicier and sharper — use less"),
                new("Dijon Mustard", "Whole Grain Mustard", SubstitutionReason.Availability, null, 1.0m, "Slightly coarser, same tangy flavour"),
                new("Hot Sauce", "Chili Flakes in Vinegar", SubstitutionReason.Availability, null, 1.0m, "Infuse for a homemade alternative"),
                new("Sriracha", "Chili Garlic Sauce", SubstitutionReason.Availability, null, 1.0m, "More garlicky, similar heat"),
                new("Sriracha", "Sambal Oelek", SubstitutionReason.Availability, null, 1.0m, "Chunkier, less sweet"),
                new("Hoisin Sauce", "Oyster Sauce with Honey", SubstitutionReason.Availability, null, 1.0m, "Mix equal parts for similar sweet-savoury depth"),
                new("Tahini", "Almond Butter", SubstitutionReason.Taste, null, 1.0m, "Milder, slightly sweeter"),
                new("Pesto", "Walnut and Basil Blend", SubstitutionReason.Allergy, "Tree Nuts", 1.0m, "Replace pine nuts with walnuts or sunflower seeds"),
                new("Caesar Dressing", "Greek Yogurt with Lemon", SubstitutionReason.Allergy, "Fish", 1.0m, "Skip anchovies, use miso for umami"),

                // ========== SWEETENERS & SUGAR ==========
                new("Sugar", "Honey", SubstitutionReason.DietaryPreference, null, 0.75m, "Use 3/4 amount, reduce liquid slightly"),
                new("Sugar", "Maple Syrup", SubstitutionReason.DietaryPreference, null, 0.75m, "Use 3/4 amount, reduce liquid slightly"),
                new("Sugar", "Coconut Sugar", SubstitutionReason.DietaryPreference, null, 1.0m, "Direct 1:1 replacement"),
                new("Sugar", "Date Syrup", SubstitutionReason.DietaryPreference, null, 0.66m, "Use 2/3 amount"),
                new("Sugar", "Stevia", SubstitutionReason.Health, null, 0.01m, "Use sparingly, very sweet"),
                new("Sugar", "Monk Fruit Sweetener", SubstitutionReason.Health, null, 1.0m, "Zero-calorie, direct swap"),
                new("Sugar", "Erythritol", SubstitutionReason.Health, null, 1.0m, "Low calorie, similar sweetness"),
                new("Brown Sugar", "Coconut Sugar", SubstitutionReason.DietaryPreference, null, 1.0m, "Lower GI, caramel-like flavour"),
                new("Brown Sugar", "White Sugar with Molasses", SubstitutionReason.Availability, null, 1.0m, "1 cup sugar + 1 tbsp molasses"),
                new("Icing Sugar", "Blended Coconut Sugar", SubstitutionReason.Health, null, 1.0m, "Blend fine — less white but works"),
                new("Honey", "Maple Syrup", SubstitutionReason.DietaryPreference, null, 1.0m, "Vegan-friendly, similar sweetness"),
                new("Honey", "Agave Syrup", SubstitutionReason.DietaryPreference, null, 0.75m, "Sweeter, use less"),
                new("Honey", "Date Paste", SubstitutionReason.Health, null, 1.0m, "Blend soaked dates — great in baking"),
                new("Maple Syrup", "Agave Syrup", SubstitutionReason.Availability, null, 0.75m, "Sweeter, use slightly less"),
                new("Agave Syrup", "Maple Syrup", SubstitutionReason.Availability, null, 1.0m, "Less sweet, different flavour"),
                new("Molasses", "Dark Treacle", SubstitutionReason.Availability, null, 1.0m, "Very similar, direct swap"),
                new("Golden Syrup", "Maple Syrup", SubstitutionReason.Availability, null, 1.0m, "Less buttery, direct swap"),
                new("Corn Syrup", "Honey", SubstitutionReason.Availability, null, 1.0m, "Thicker, adds flavour"),
                new("Corn Syrup", "Agave Syrup", SubstitutionReason.Availability, null, 1.0m, "Lighter flavour, similar consistency"),

                // ========== BAKING ESSENTIALS ==========
                new("Baking Powder", "Baking Soda", SubstitutionReason.Availability, null, 0.25m, "Use 1/4 the amount + add cream of tartar"),
                new("Baking Soda", "Baking Powder", SubstitutionReason.Availability, null, 3.0m, "Use 3x the amount — weaker leavening"),
                new("Cream of Tartar", "Lemon Juice", SubstitutionReason.Availability, null, 2.0m, "Use double the amount"),
                new("Cream of Tartar", "White Vinegar", SubstitutionReason.Availability, null, 2.0m, "Works to stabilise egg whites"),
                new("Cocoa Powder", "Carob Powder", SubstitutionReason.Taste, null, 1.0m, "Naturally sweet, caffeine-free"),
                new("Dark Chocolate", "Cocoa Powder with Oil", SubstitutionReason.Availability, null, 1.0m, "3 tbsp cocoa + 1 tbsp oil per oz of chocolate"),
                new("Dark Chocolate", "Carob Chips", SubstitutionReason.Health, null, 1.0m, "Caffeine-free, naturally sweeter"),
                new("Milk Chocolate", "Dark Chocolate with Milk", SubstitutionReason.Availability, null, 1.0m, "Melt together for a closer match"),
                new("White Chocolate", "Cocoa Butter with Milk Powder", SubstitutionReason.Availability, null, 1.0m, "Combine for homemade white chocolate"),
                new("Shortening", "Coconut Oil", SubstitutionReason.DietaryPreference, null, 1.0m, "Solid at room temp, dairy-free"),
                new("Shortening", "Butter", SubstitutionReason.Availability, null, 1.0m, "Adds flavour, direct swap"),
                new("Bread Yeast", "Sourdough Starter", SubstitutionReason.Availability, null, 1.0m, "Use 100g starter per 7g yeast — add flavour complexity"),
                new("Instant Yeast", "Active Dry Yeast", SubstitutionReason.Availability, null, 1.25m, "Use 25% more, proof in water first"),

                // ========== STOCKS & LIQUIDS ==========
                new("Chicken Stock", "Vegetable Stock", SubstitutionReason.DietaryPreference, null, 1.0m, "Vegetarian/vegan alternative"),
                new("Chicken Stock", "Mushroom Stock", SubstitutionReason.DietaryPreference, null, 1.0m, "Deeper umami, works in risotto and braises"),
                new("Beef Stock", "Mushroom Stock", SubstitutionReason.DietaryPreference, null, 1.0m, "Rich umami flavor"),
                new("Beef Stock", "Dark Beer", SubstitutionReason.Taste, null, 1.0m, "Adds bitterness and depth in braises and stews"),
                new("Vegetable Stock", "Water with Miso", SubstitutionReason.Availability, null, 1.0m, "1 tbsp miso per cup of water"),
                new("White Wine", "Chicken Stock with Lemon", SubstitutionReason.Availability, null, 1.0m, "Non-alcoholic alternative in cooking"),
                new("White Wine", "Apple Juice with Vinegar", SubstitutionReason.Availability, null, 1.0m, "Mimics wine's sweetness and acidity"),
                new("Red Wine", "Beef Stock with Balsamic", SubstitutionReason.Availability, null, 1.0m, "Deep colour and acidity without alcohol"),
                new("Red Wine", "Pomegranate Juice", SubstitutionReason.Availability, null, 1.0m, "Fruity and deep, good in braises"),
                new("Beer", "Sparkling Apple Juice", SubstitutionReason.Availability, null, 1.0m, "Works in batters and breads"),
                new("Brandy", "Apple Juice with Vanilla", SubstitutionReason.Availability, null, 1.0m, "Non-alcoholic, works in desserts"),
                new("Rum", "Pineapple Juice with Vanilla", SubstitutionReason.Availability, null, 1.0m, "Tropical substitute in baking"),

                // ========== LEGUMES ==========
                new("Chickpeas", "White Beans", SubstitutionReason.Availability, null, 1.0m, "Similar mild flavour and texture"),
                new("Chickpeas", "Lentils", SubstitutionReason.Availability, null, 1.0m, "Cook lentils separately first"),
                new("Chickpeas", "Fava Beans", SubstitutionReason.Availability, null, 1.0m, "Earthier flavour, works in dips"),
                new("Lentils", "Chickpeas", SubstitutionReason.Availability, null, 1.0m, "Heartier texture"),
                new("Lentils", "Split Peas", SubstitutionReason.Availability, null, 1.0m, "Similar cooking time"),
                new("Lentils", "Black Beans", SubstitutionReason.Availability, null, 1.0m, "Firmer texture, works in tacos and burritos"),
                new("Black Beans", "Kidney Beans", SubstitutionReason.Availability, null, 1.0m, "Slightly firmer, works in chillis and stews"),
                new("Black Beans", "Pinto Beans", SubstitutionReason.Availability, null, 1.0m, "Creamier, great in Mexican dishes"),
                new("Kidney Beans", "Black Beans", SubstitutionReason.Availability, null, 1.0m, "Similar hearty texture"),
                new("Kidney Beans", "Chickpeas", SubstitutionReason.Availability, null, 1.0m, "Milder, works in curries and salads"),
                new("Cannellini Beans", "Butter Beans", SubstitutionReason.Availability, null, 1.0m, "Creamier, works in soups and dips"),
                new("Cannellini Beans", "Navy Beans", SubstitutionReason.Availability, null, 1.0m, "Smaller, similar creamy texture"),
                new("Peas", "Edamame", SubstitutionReason.Availability, null, 1.0m, "Firmer, higher protein"),
                new("Peas", "Broad Beans", SubstitutionReason.Availability, null, 1.0m, "Earthier, slightly starchier"),

                // ========== HALAL & RELIGIOUS ==========
                new("Gelatin", "Agar Agar", SubstitutionReason.Religious, null, 0.5m, "Use half the amount, plant-based"),
                new("Gelatin", "Halal Gelatin", SubstitutionReason.Religious, null, 1.0m, "Direct replacement"),
                new("Gelatin", "Pectin", SubstitutionReason.Religious, null, 1.0m, "Works in jams and jellies — fruit-based"),
                new("Lard", "Vegetable Shortening", SubstitutionReason.Religious, null, 1.0m, "Plant-based fat"),
                new("Lard", "Coconut Oil", SubstitutionReason.Religious, null, 1.0m, "Solid at room temp"),
                new("Lard", "Ghee", SubstitutionReason.Religious, null, 1.0m, "Works in pastry and frying"),
                new("Pork Gelatine", "Agar Agar", SubstitutionReason.Religious, null, 0.5m, "Halal and vegan alternative"),
                new("Non-Halal Chicken", "Halal Chicken", SubstitutionReason.Religious, null, 1.0m, "Direct swap"),
                new("Non-Halal Beef", "Halal Beef", SubstitutionReason.Religious, null, 1.0m, "Direct swap"),
                new("Alcohol-Based Vanilla", "Vanilla Bean Paste", SubstitutionReason.Religious, null, 1.0m, "Alcohol-free alternative"),
                new("Alcohol-Based Vanilla", "Halal Vanilla Extract", SubstitutionReason.Religious, null, 1.0m, "Water or glycerin-based extract"),
                new("Wine Vinegar", "Apple Cider Vinegar", SubstitutionReason.Religious, null, 1.0m, "Alcohol-derived alternative avoided by some"),
                new("Mirin", "Rice Vinegar with Sugar", SubstitutionReason.Religious, null, 1.0m, "Non-alcoholic — mix 1 tbsp rice vinegar + 1 tsp sugar"),
                new("Sake", "Water with Rice Vinegar", SubstitutionReason.Religious, null, 1.0m, "Non-alcoholic alternative for marinades"),
            };
        }

        private record SubstitutionSeedData(
            string Original,
            string Substitute,
            SubstitutionReason Reason,
            string? AllergyName,
            decimal Ratio,
            string Notes);
    }
}
