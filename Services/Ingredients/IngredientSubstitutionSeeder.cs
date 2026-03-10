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

        public async Task SeedAsync(bool force = false, CancellationToken ct = default)
        {
            // Check if already seeded (expect ~78 substitutions)
            var existingCount = await _db.IngredientSubstitutions.CountAsync(ct);
            if (!force && existingCount >= 70)
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

            // Create substitutions
            var substitutions = new List<IngredientSubstitution>();

            foreach (var data in substitutionData)
            {
                var originalId = ingredientCache[data.Original.ToLowerInvariant()];
                var substituteId = ingredientCache[data.Substitute.ToLowerInvariant()];

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
                    ConfidenceScore = 85, // High confidence for curated substitutions
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
                // ========== DAIRY SUBSTITUTIONS ==========
                // Milk alternatives
                new("Milk", "Oat Milk", SubstitutionReason.Allergy, "Milk", 1.0m, "Creamy texture, great for baking and coffee"),
                new("Milk", "Almond Milk", SubstitutionReason.Allergy, "Milk", 1.0m, "Lighter texture, slightly nutty flavor"),
                new("Milk", "Soy Milk", SubstitutionReason.Allergy, "Milk", 1.0m, "High protein, good for baking"),
                new("Milk", "Coconut Milk", SubstitutionReason.Allergy, "Milk", 1.0m, "Rich and creamy, adds coconut flavor"),

                // Butter alternatives
                new("Butter", "Coconut Oil", SubstitutionReason.Allergy, "Milk", 1.0m, "Solid at room temp, good for baking"),
                new("Butter", "Olive Oil", SubstitutionReason.Allergy, "Milk", 0.75m, "Use 3/4 the amount, best for savory dishes"),
                new("Butter", "Vegan Butter", SubstitutionReason.Allergy, "Milk", 1.0m, "Direct 1:1 replacement"),
                new("Butter", "Avocado", SubstitutionReason.Allergy, "Milk", 1.0m, "Great for baking, adds moisture"),
                new("Butter", "Ghee", SubstitutionReason.DietaryPreference, null, 1.0m, "Clarified butter, lactose-free"),

                // Cream alternatives
                new("Heavy Cream", "Coconut Cream", SubstitutionReason.Allergy, "Milk", 1.0m, "Thick and rich, slight coconut taste"),
                new("Heavy Cream", "Cashew Cream", SubstitutionReason.Allergy, "Milk", 1.0m, "Blend soaked cashews with water"),
                new("Sour Cream", "Greek Yogurt", SubstitutionReason.DietaryPreference, null, 1.0m, "Similar tang, more protein"),
                new("Sour Cream", "Coconut Yogurt", SubstitutionReason.Allergy, "Milk", 1.0m, "Dairy-free alternative"),

                // Cheese alternatives
                new("Parmesan", "Nutritional Yeast", SubstitutionReason.Allergy, "Milk", 0.5m, "Use half the amount, cheesy umami flavor"),
                new("Cream Cheese", "Cashew Cream Cheese", SubstitutionReason.Allergy, "Milk", 1.0m, "Blend soaked cashews with lemon"),
                new("Ricotta", "Tofu Ricotta", SubstitutionReason.Allergy, "Milk", 1.0m, "Blend firm tofu with lemon and salt"),

                // ========== EGG SUBSTITUTIONS ==========
                new("Egg", "Flax Egg", SubstitutionReason.Allergy, "Eggs", 1.0m, "1 tbsp ground flax + 3 tbsp water per egg"),
                new("Egg", "Chia Egg", SubstitutionReason.Allergy, "Eggs", 1.0m, "1 tbsp chia seeds + 3 tbsp water per egg"),
                new("Egg", "Applesauce", SubstitutionReason.Allergy, "Eggs", 0.25m, "1/4 cup per egg, adds sweetness"),
                new("Egg", "Mashed Banana", SubstitutionReason.Allergy, "Eggs", 0.25m, "1/4 cup per egg, adds banana flavor"),
                new("Egg", "Silken Tofu", SubstitutionReason.Allergy, "Eggs", 0.25m, "1/4 cup blended per egg"),
                new("Egg", "Aquafaba", SubstitutionReason.Allergy, "Eggs", 3.0m, "3 tbsp chickpea water per egg"),
                new("Egg", "Commercial Egg Replacer", SubstitutionReason.Allergy, "Eggs", 1.0m, "Follow package instructions"),

                // ========== GLUTEN SUBSTITUTIONS ==========
                new("All-Purpose Flour", "Almond Flour", SubstitutionReason.Allergy, "Gluten", 1.0m, "May need binding agent, denser result"),
                new("All-Purpose Flour", "Oat Flour", SubstitutionReason.Allergy, "Gluten", 1.0m, "Use certified gluten-free oats"),
                new("All-Purpose Flour", "Rice Flour", SubstitutionReason.Allergy, "Gluten", 1.0m, "Lighter texture, blend with other GF flours"),
                new("All-Purpose Flour", "Coconut Flour", SubstitutionReason.Allergy, "Gluten", 0.25m, "Use 1/4 amount, very absorbent"),
                new("All-Purpose Flour", "Gluten-Free Flour Blend", SubstitutionReason.Allergy, "Gluten", 1.0m, "Best 1:1 replacement"),

                new("Bread Crumbs", "Gluten-Free Bread Crumbs", SubstitutionReason.Allergy, "Gluten", 1.0m, "Direct replacement"),
                new("Bread Crumbs", "Crushed Rice Crackers", SubstitutionReason.Allergy, "Gluten", 1.0m, "Pulse in food processor"),
                new("Bread Crumbs", "Almond Meal", SubstitutionReason.Allergy, "Gluten", 1.0m, "Adds nutty flavor"),

                new("Pasta", "Rice Noodles", SubstitutionReason.Allergy, "Gluten", 1.0m, "Cook according to package"),
                new("Pasta", "Zucchini Noodles", SubstitutionReason.Allergy, "Gluten", 1.0m, "Low carb option, spiralize fresh"),
                new("Pasta", "Gluten-Free Pasta", SubstitutionReason.Allergy, "Gluten", 1.0m, "Direct replacement"),

                new("Soy Sauce", "Tamari", SubstitutionReason.Allergy, "Gluten", 1.0m, "Gluten-free soy sauce alternative"),

                // ========== NUT SUBSTITUTIONS ==========
                new("Peanut Butter", "Sunflower Seed Butter", SubstitutionReason.Allergy, "Peanuts", 1.0m, "Nut-free, similar texture"),
                new("Peanut Butter", "Tahini", SubstitutionReason.Allergy, "Peanuts", 1.0m, "Sesame-based, slightly bitter"),
                new("Peanut Butter", "Soy Nut Butter", SubstitutionReason.Allergy, "Peanuts", 1.0m, "Made from roasted soybeans"),

                new("Almonds", "Sunflower Seeds", SubstitutionReason.Allergy, "Tree Nuts", 1.0m, "Similar crunch"),
                new("Almonds", "Pumpkin Seeds", SubstitutionReason.Allergy, "Tree Nuts", 1.0m, "Pepitas work well"),
                new("Walnuts", "Sunflower Seeds", SubstitutionReason.Allergy, "Tree Nuts", 1.0m, "For baking and salads"),
                new("Pine Nuts", "Sunflower Seeds", SubstitutionReason.Allergy, "Tree Nuts", 1.0m, "Good for pesto"),

                new("Almond Milk", "Oat Milk", SubstitutionReason.Allergy, "Tree Nuts", 1.0m, "Nut-free milk alternative"),
                new("Almond Milk", "Rice Milk", SubstitutionReason.Allergy, "Tree Nuts", 1.0m, "Lighter, nut-free"),
                new("Almond Flour", "Sunflower Seed Flour", SubstitutionReason.Allergy, "Tree Nuts", 1.0m, "May turn green from chlorophyll"),

                // ========== SOY SUBSTITUTIONS ==========
                new("Tofu", "Chickpeas", SubstitutionReason.Allergy, "Soy", 1.0m, "Mash for similar texture"),
                new("Tofu", "Tempeh", SubstitutionReason.DietaryPreference, null, 1.0m, "Fermented, nuttier flavor"),
                new("Soy Milk", "Oat Milk", SubstitutionReason.Allergy, "Soy", 1.0m, "Creamy, soy-free"),
                new("Soy Milk", "Coconut Milk", SubstitutionReason.Allergy, "Soy", 1.0m, "Rich and creamy"),
                new("Soy Sauce", "Coconut Aminos", SubstitutionReason.Allergy, "Soy", 1.0m, "Soy-free, slightly sweeter"),
                new("Edamame", "Lima Beans", SubstitutionReason.Allergy, "Soy", 1.0m, "Similar texture"),
                new("Miso", "Chickpea Miso", SubstitutionReason.Allergy, "Soy", 1.0m, "Soy-free fermented paste"),

                // ========== COMMON COOKING SUBSTITUTIONS ==========
                new("Sugar", "Honey", SubstitutionReason.DietaryPreference, null, 0.75m, "Use 3/4 amount, reduce liquid slightly"),
                new("Sugar", "Maple Syrup", SubstitutionReason.DietaryPreference, null, 0.75m, "Use 3/4 amount, reduce liquid slightly"),
                new("Sugar", "Coconut Sugar", SubstitutionReason.DietaryPreference, null, 1.0m, "Direct 1:1 replacement"),
                new("Sugar", "Date Syrup", SubstitutionReason.DietaryPreference, null, 0.66m, "Use 2/3 amount"),
                new("Sugar", "Stevia", SubstitutionReason.Health, null, 0.01m, "Use sparingly, very sweet"),

                new("Vegetable Oil", "Olive Oil", SubstitutionReason.DietaryPreference, null, 1.0m, "Better for savory dishes"),
                new("Vegetable Oil", "Coconut Oil", SubstitutionReason.DietaryPreference, null, 1.0m, "Solid at room temp"),
                new("Vegetable Oil", "Avocado Oil", SubstitutionReason.DietaryPreference, null, 1.0m, "High smoke point"),

                new("Chicken Stock", "Vegetable Stock", SubstitutionReason.DietaryPreference, null, 1.0m, "Vegetarian/vegan alternative"),
                new("Beef Stock", "Mushroom Stock", SubstitutionReason.DietaryPreference, null, 1.0m, "Rich umami flavor"),

                new("Mayonnaise", "Greek Yogurt", SubstitutionReason.DietaryPreference, null, 1.0m, "Lower fat, tangy"),
                new("Mayonnaise", "Avocado", SubstitutionReason.DietaryPreference, null, 1.0m, "Mash for spreads"),
                new("Mayonnaise", "Vegan Mayo", SubstitutionReason.Allergy, "Eggs", 1.0m, "Egg-free alternative"),

                // ========== HALAL SUBSTITUTIONS ==========
                new("Gelatin", "Agar Agar", SubstitutionReason.Religious, null, 0.5m, "Use half the amount, plant-based"),
                new("Gelatin", "Halal Gelatin", SubstitutionReason.Religious, null, 1.0m, "Direct replacement"),
                new("Lard", "Vegetable Shortening", SubstitutionReason.Religious, null, 1.0m, "Plant-based fat"),
                new("Lard", "Coconut Oil", SubstitutionReason.Religious, null, 1.0m, "Solid at room temp"),

                // ========== FISH/SHELLFISH SUBSTITUTIONS ==========
                new("Fish Sauce", "Soy Sauce", SubstitutionReason.Allergy, "Fish", 1.0m, "Less funky, add a pinch of salt"),
                new("Fish Sauce", "Coconut Aminos", SubstitutionReason.Allergy, "Fish", 1.5m, "Use a bit more, sweeter"),
                new("Anchovy Paste", "Capers", SubstitutionReason.Allergy, "Fish", 1.0m, "For umami in Caesar dressing"),
                new("Oyster Sauce", "Mushroom Sauce", SubstitutionReason.Allergy, "Shellfish", 1.0m, "Vegetarian oyster sauce"),
                new("Shrimp", "Hearts of Palm", SubstitutionReason.Allergy, "Shellfish", 1.0m, "Similar texture when chopped"),

                // ========== POULTRY ==========
                new("Chicken", "Tofu", SubstitutionReason.DietaryPreference, null, 1.0m, "Press firm tofu and cube or slice before cooking"),
                new("Chicken", "Chickpeas", SubstitutionReason.DietaryPreference, null, 1.0m, "Great in curries and stews"),
                new("Chicken", "Cauliflower", SubstitutionReason.DietaryPreference, null, 1.0m, "Roast first for best texture"),
                new("Chicken", "Turkey", SubstitutionReason.DietaryPreference, null, 1.0m, "Direct 1:1 replacement in most recipes"),
                new("Chicken", "Lamb", SubstitutionReason.DietaryPreference, null, 1.0m, "Richer flavour, adjust spices accordingly"),
                new("Chicken", "Jackfruit", SubstitutionReason.DietaryPreference, null, 1.0m, "Young green jackfruit mimics pulled chicken"),

                // ========== BEEF / RED MEAT ==========
                new("Beef", "Lamb", SubstitutionReason.DietaryPreference, null, 1.0m, "Similar cooking times, richer flavour"),
                new("Beef", "Chicken", SubstitutionReason.DietaryPreference, null, 1.0m, "Leaner, reduce cooking time"),
                new("Beef", "Lentils", SubstitutionReason.DietaryPreference, null, 1.0m, "Great in bolognese and stews"),
                new("Beef", "Mushrooms", SubstitutionReason.DietaryPreference, null, 1.0m, "Use portobello or mixed mushrooms for umami"),
                new("Beef", "Jackfruit", SubstitutionReason.DietaryPreference, null, 1.0m, "For pulled or shredded beef dishes"),
                new("Ground Beef", "Ground Lamb", SubstitutionReason.DietaryPreference, null, 1.0m, "Works well in burgers and kebabs"),
                new("Ground Beef", "Ground Turkey", SubstitutionReason.DietaryPreference, null, 1.0m, "Leaner option, similar texture"),
                new("Ground Beef", "Lentils", SubstitutionReason.DietaryPreference, null, 1.0m, "Cook until soft, season well"),

                // ========== AROMATICS ==========
                new("Garlic", "Garlic Powder", SubstitutionReason.Availability, null, 0.125m, "1/8 tsp powder per fresh clove"),
                new("Garlic", "Shallots", SubstitutionReason.Taste, null, 1.0m, "Milder flavour, use same amount"),
                new("Ginger", "Ground Ginger", SubstitutionReason.Availability, null, 0.25m, "1/4 tsp ground per 1 tsp fresh"),
                new("Ginger", "Galangal", SubstitutionReason.Availability, null, 1.0m, "Similar but sharper, use same amount"),
                new("Onion", "Shallots", SubstitutionReason.Taste, null, 1.0m, "Sweeter, milder flavour"),
                new("Onion", "Leeks", SubstitutionReason.Taste, null, 1.0m, "Use white and light green parts only"),
                new("Onion", "Onion Powder", SubstitutionReason.Availability, null, 0.25m, "1/4 tsp powder per small onion"),

                // ========== ACIDS & CITRUS ==========
                new("Lemon Juice", "Lime Juice", SubstitutionReason.Availability, null, 1.0m, "Slightly more tart, works in most recipes"),
                new("Lemon Juice", "Apple Cider Vinegar", SubstitutionReason.Availability, null, 0.5m, "Use half the amount, more pungent"),
                new("Lemon Juice", "White Wine Vinegar", SubstitutionReason.Availability, null, 0.5m, "Mild acidity, good for dressings"),
                new("Lime Juice", "Lemon Juice", SubstitutionReason.Availability, null, 1.0m, "Works in most recipes"),

                // ========== OILS ==========
                new("Olive Oil", "Avocado Oil", SubstitutionReason.DietaryPreference, null, 1.0m, "Higher smoke point, neutral flavour"),
                new("Olive Oil", "Coconut Oil", SubstitutionReason.DietaryPreference, null, 1.0m, "Solid at room temp, adds coconut notes"),
                new("Olive Oil", "Vegetable Oil", SubstitutionReason.Availability, null, 1.0m, "Neutral flavour, direct replacement"),
                new("Ghee", "Butter", SubstitutionReason.Availability, null, 1.0m, "Contains lactose unlike ghee"),
                new("Ghee", "Coconut Oil", SubstitutionReason.DietaryPreference, null, 1.0m, "Dairy-free, high smoke point"),

                // ========== TOMATOES ==========
                new("Tomatoes", "Canned Tomatoes", SubstitutionReason.Availability, null, 1.0m, "Use drained for fresh tomato texture"),
                new("Tomatoes", "Tomato Paste", SubstitutionReason.Availability, null, 0.25m, "Dilute with water, more concentrated"),
                new("Tomato Paste", "Tomato Puree", SubstitutionReason.Availability, null, 2.0m, "Use double the amount"),
                new("Tomato Puree", "Blended Canned Tomatoes", SubstitutionReason.Availability, null, 1.0m, "Blend and strain for smooth result"),

                // ========== GRAINS & RICE ==========
                new("Rice", "Quinoa", SubstitutionReason.Health, null, 1.0m, "Higher protein, nuttier flavour"),
                new("Rice", "Cauliflower Rice", SubstitutionReason.Health, null, 1.0m, "Low carb, pulse cauliflower in food processor"),
                new("Rice", "Bulgur Wheat", SubstitutionReason.DietaryPreference, null, 1.0m, "Nuttier flavour, slightly chewy"),
                new("Basmati Rice", "Long-Grain Rice", SubstitutionReason.Availability, null, 1.0m, "Similar texture, less aromatic"),
                new("Basmati Rice", "Jasmine Rice", SubstitutionReason.Availability, null, 1.0m, "Slightly stickier, fragrant"),

                // ========== HERBS & SPICES ==========
                new("Fresh Cilantro", "Fresh Parsley", SubstitutionReason.Taste, null, 1.0m, "Milder flavour, no citrus notes"),
                new("Fresh Cilantro", "Thai Basil", SubstitutionReason.Taste, null, 1.0m, "Slightly different but works in Asian dishes"),
                new("Cumin", "Caraway Seeds", SubstitutionReason.Availability, null, 1.0m, "Similar earthy flavour"),
                new("Cumin", "Coriander", SubstitutionReason.Availability, null, 1.0m, "Lighter, citrusy alternative"),
                new("Coriander", "Cumin", SubstitutionReason.Availability, null, 1.0m, "Earthier, use same amount"),
                new("Turmeric", "Saffron", SubstitutionReason.Availability, null, 0.25m, "For colour only, flavour differs"),
                new("Paprika", "Chili Powder", SubstitutionReason.Availability, null, 0.5m, "Hotter, use less"),

                // ========== DAIRY EXTRAS ==========
                new("Greek Yogurt", "Plain Yogurt", SubstitutionReason.Availability, null, 1.0m, "Thinner consistency, same tangy flavour"),
                new("Greek Yogurt", "Sour Cream", SubstitutionReason.Availability, null, 1.0m, "Similar tang and texture"),
                new("Yogurt", "Coconut Yogurt", SubstitutionReason.Allergy, "Milk", 1.0m, "Dairy-free, slight coconut flavour"),

                // ========== LEGUMES ==========
                new("Chickpeas", "White Beans", SubstitutionReason.Availability, null, 1.0m, "Similar mild flavour and texture"),
                new("Chickpeas", "Lentils", SubstitutionReason.Availability, null, 1.0m, "Cook lentils separately first"),
                new("Lentils", "Chickpeas", SubstitutionReason.Availability, null, 1.0m, "Heartier texture"),
                new("Lentils", "Split Peas", SubstitutionReason.Availability, null, 1.0m, "Similar cooking time"),

                // ========== BAKING ==========
                new("Baking Powder", "Baking Soda", SubstitutionReason.Availability, null, 0.25m, "Use 1/4 the amount + add cream of tartar"),
                new("Honey", "Maple Syrup", SubstitutionReason.DietaryPreference, null, 1.0m, "Vegan-friendly, similar sweetness"),
                new("Honey", "Agave Syrup", SubstitutionReason.DietaryPreference, null, 0.75m, "Sweeter, use less"),
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
