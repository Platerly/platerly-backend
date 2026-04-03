using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Ingredients
{
    /// <summary>
    /// Seeds the database with a comprehensive ingredient category taxonomy.
    /// Hierarchical structure for grouping, searching, and B2B analytics.
    /// </summary>
    public class IngredientCategorySeeder
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<IngredientCategorySeeder> _logger;

        public IngredientCategorySeeder(
            PlaterlyDbContext db,
            ILogger<IngredientCategorySeeder> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Seeds the hierarchical ingredient category taxonomy. Skips entirely if any
        /// <c>IngredientCategories</c> rows already exist (one-time seed guard).
        /// </summary>
        public async Task SeedAsync(CancellationToken ct = default)
        {
            // Check if already seeded
            if (await _db.IngredientCategories.AnyAsync(ct))
            {
                _logger.LogInformation("Ingredient categories already seeded, skipping");
                return;
            }

            var categories = new List<IngredientCategory>();

            // ========== ROOT CATEGORIES ==========
            var proteins = CreateCategory("Proteins", "proteins", "High-protein food sources", sortOrder: 1);
            var dairy = CreateCategory("Dairy & Eggs", "dairy-eggs", "Milk products and eggs", sortOrder: 2, commonAllergens: "Milk,Eggs");
            var grains = CreateCategory("Grains & Starches", "grains-starches", "Cereals, rice, pasta, and starchy foods", sortOrder: 3, commonAllergens: "Gluten,Wheat");
            var vegetables = CreateCategory("Vegetables", "vegetables", "Fresh and preserved vegetables", sortOrder: 4);
            var fruits = CreateCategory("Fruits", "fruits", "Fresh and dried fruits", sortOrder: 5);
            var herbsSpices = CreateCategory("Herbs & Spices", "herbs-spices", "Flavoring herbs and spices", sortOrder: 6);
            var fatsOils = CreateCategory("Fats & Oils", "fats-oils", "Cooking fats and oils", sortOrder: 7);
            var sweeteners = CreateCategory("Sweeteners", "sweeteners", "Sugars and natural sweeteners", sortOrder: 8);
            var condiments = CreateCategory("Condiments & Sauces", "condiments-sauces", "Prepared sauces and condiments", sortOrder: 9);
            var nutsSeeds = CreateCategory("Nuts & Seeds", "nuts-seeds", "Tree nuts, peanuts, and seeds", sortOrder: 10, commonAllergens: "Tree Nuts,Peanuts,Sesame");
            var beverages = CreateCategory("Beverages", "beverages", "Drink ingredients and bases", sortOrder: 11);
            var bakingEssentials = CreateCategory("Baking Essentials", "baking-essentials", "Flour, leavening, and baking ingredients", sortOrder: 12, commonAllergens: "Gluten,Wheat");

            categories.AddRange(new[] { proteins, dairy, grains, vegetables, fruits, herbsSpices, fatsOils, sweeteners, condiments, nutsSeeds, beverages, bakingEssentials });

            // Add root categories first to get IDs
            await _db.IngredientCategories.AddRangeAsync(categories, ct);
            await _db.SaveChangesAsync(ct);

            // ========== PROTEINS SUBCATEGORIES ==========
            var proteinSubs = new List<IngredientCategory>
            {
                CreateSubCategory("Beef", "beef", "Cow meat products", proteins.Id, 1),
                CreateSubCategory("Lamb & Mutton", "lamb-mutton", "Sheep meat products", proteins.Id, 2),
                CreateSubCategory("Poultry", "poultry", "Chicken, turkey, and other birds", proteins.Id, 3),
                CreateSubCategory("Seafood - Fish", "seafood-fish", "Fish varieties", proteins.Id, 4, commonAllergens: "Fish"),
                CreateSubCategory("Seafood - Shellfish", "seafood-shellfish", "Shrimp, crab, lobster, etc.", proteins.Id, 5, commonAllergens: "Shellfish"),
                CreateSubCategory("Legumes", "legumes", "Beans, lentils, chickpeas", proteins.Id, 6, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Tofu & Soy", "tofu-soy", "Soy-based proteins", proteins.Id, 7, commonAllergens: "Soy", dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Game Meat", "game-meat", "Venison, rabbit, etc.", proteins.Id, 8),
            };
            await _db.IngredientCategories.AddRangeAsync(proteinSubs, ct);

            // ========== DAIRY SUBCATEGORIES ==========
            var dairySubs = new List<IngredientCategory>
            {
                CreateSubCategory("Milk", "milk", "Liquid milk products", dairy.Id, 1, commonAllergens: "Milk"),
                CreateSubCategory("Cheese - Soft", "cheese-soft", "Brie, mozzarella, ricotta", dairy.Id, 2, commonAllergens: "Milk"),
                CreateSubCategory("Cheese - Hard", "cheese-hard", "Parmesan, cheddar, gouda", dairy.Id, 3, commonAllergens: "Milk"),
                CreateSubCategory("Yogurt", "yogurt", "Yogurt and cultured dairy", dairy.Id, 4, commonAllergens: "Milk"),
                CreateSubCategory("Cream & Butter", "cream-butter", "Heavy cream, butter, ghee", dairy.Id, 5, commonAllergens: "Milk"),
                CreateSubCategory("Eggs", "eggs", "Chicken and other eggs", dairy.Id, 6, commonAllergens: "Eggs"),
                CreateSubCategory("Dairy Alternatives", "dairy-alternatives", "Plant-based milk and cheese", dairy.Id, 7, dietaryFlags: "vegan"),
            };
            await _db.IngredientCategories.AddRangeAsync(dairySubs, ct);

            // ========== GRAINS SUBCATEGORIES ==========
            var grainSubs = new List<IngredientCategory>
            {
                CreateSubCategory("Rice", "rice", "White, brown, basmati, jasmine", grains.Id, 1, dietaryFlags: "gluten-free"),
                CreateSubCategory("Pasta", "pasta", "Italian pasta varieties", grains.Id, 2, commonAllergens: "Gluten,Wheat"),
                CreateSubCategory("Noodles", "noodles", "Asian noodles", grains.Id, 3, commonAllergens: "Gluten,Wheat"),
                CreateSubCategory("Bread", "bread", "Bread and rolls", grains.Id, 4, commonAllergens: "Gluten,Wheat"),
                CreateSubCategory("Couscous & Bulgur", "couscous-bulgur", "Middle Eastern grains", grains.Id, 5, commonAllergens: "Gluten,Wheat"),
                CreateSubCategory("Quinoa & Ancient Grains", "quinoa-ancient-grains", "Quinoa, farro, barley", grains.Id, 6),
                CreateSubCategory("Potatoes", "potatoes", "All potato varieties", grains.Id, 7, dietaryFlags: "gluten-free,vegan"),
                CreateSubCategory("Oats", "oats", "Rolled oats, steel-cut, oat flour", grains.Id, 8),
            };
            await _db.IngredientCategories.AddRangeAsync(grainSubs, ct);

            // ========== VEGETABLES SUBCATEGORIES ==========
            var vegSubs = new List<IngredientCategory>
            {
                CreateSubCategory("Leafy Greens", "leafy-greens", "Spinach, kale, lettuce", vegetables.Id, 1, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Root Vegetables", "root-vegetables", "Carrots, beets, turnips", vegetables.Id, 2, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Alliums", "alliums", "Onion, garlic, shallots, leeks", vegetables.Id, 3, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Cruciferous", "cruciferous", "Broccoli, cauliflower, cabbage", vegetables.Id, 4, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Nightshades", "nightshades", "Tomatoes, peppers, eggplant", vegetables.Id, 5, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Squash & Gourds", "squash-gourds", "Zucchini, pumpkin, butternut", vegetables.Id, 6, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Mushrooms", "mushrooms", "Button, shiitake, portobello", vegetables.Id, 7, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Beans & Peas (Fresh)", "beans-peas-fresh", "Green beans, snap peas", vegetables.Id, 8, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Corn", "corn", "Sweet corn, corn on cob", vegetables.Id, 9, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Pickled & Preserved", "pickled-preserved", "Pickles, olives, capers", vegetables.Id, 10, dietaryFlags: "vegan,vegetarian"),
            };
            await _db.IngredientCategories.AddRangeAsync(vegSubs, ct);

            // ========== FRUITS SUBCATEGORIES ==========
            var fruitSubs = new List<IngredientCategory>
            {
                CreateSubCategory("Citrus", "citrus", "Lemons, oranges, limes", fruits.Id, 1, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Berries", "berries", "Strawberries, blueberries, raspberries", fruits.Id, 2, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Stone Fruits", "stone-fruits", "Peaches, plums, cherries", fruits.Id, 3, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Tropical Fruits", "tropical-fruits", "Mango, pineapple, papaya", fruits.Id, 4, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Apples & Pears", "apples-pears", "Apple and pear varieties", fruits.Id, 5, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Melons", "melons", "Watermelon, cantaloupe, honeydew", fruits.Id, 6, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Grapes", "grapes", "Table grapes and raisins", fruits.Id, 7, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Dried Fruits", "dried-fruits", "Dates, figs, apricots", fruits.Id, 8, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Bananas", "bananas", "Bananas and plantains", fruits.Id, 9, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Avocados", "avocados", "Avocado varieties", fruits.Id, 10, dietaryFlags: "vegan,vegetarian"),
            };
            await _db.IngredientCategories.AddRangeAsync(fruitSubs, ct);

            // ========== HERBS & SPICES SUBCATEGORIES ==========
            var herbSubs = new List<IngredientCategory>
            {
                CreateSubCategory("Fresh Herbs", "fresh-herbs", "Basil, cilantro, parsley, mint", herbsSpices.Id, 1, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Dried Herbs", "dried-herbs", "Oregano, thyme, rosemary", herbsSpices.Id, 2, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Ground Spices", "ground-spices", "Cumin, paprika, turmeric", herbsSpices.Id, 3, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Whole Spices", "whole-spices", "Cinnamon sticks, cardamom pods", herbsSpices.Id, 4, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Spice Blends", "spice-blends", "Curry powder, garam masala, za'atar", herbsSpices.Id, 5, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Chili & Peppers (Dried)", "chili-peppers-dried", "Cayenne, red pepper flakes", herbsSpices.Id, 6, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Salt & Pepper", "salt-pepper", "Sea salt, black pepper, specialty salts", herbsSpices.Id, 7, dietaryFlags: "vegan,vegetarian"),
            };
            await _db.IngredientCategories.AddRangeAsync(herbSubs, ct);

            // ========== FATS & OILS SUBCATEGORIES ==========
            var fatSubs = new List<IngredientCategory>
            {
                CreateSubCategory("Olive Oil", "olive-oil", "Extra virgin, light, regular", fatsOils.Id, 1, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Vegetable Oils", "vegetable-oils", "Canola, sunflower, corn oil", fatsOils.Id, 2, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Nut Oils", "nut-oils", "Sesame, peanut, walnut oil", fatsOils.Id, 3, commonAllergens: "Tree Nuts,Peanuts,Sesame", dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Coconut Oil & Products", "coconut-oil", "Coconut oil, cream, milk", fatsOils.Id, 4, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Animal Fats (Halal)", "animal-fats-halal", "Ghee, tallow (halal-certified)", fatsOils.Id, 5, commonAllergens: "Milk"),
            };
            await _db.IngredientCategories.AddRangeAsync(fatSubs, ct);

            // ========== SWEETENERS SUBCATEGORIES ==========
            var sweetSubs = new List<IngredientCategory>
            {
                CreateSubCategory("Sugar", "sugar", "White, brown, powdered sugar", sweeteners.Id, 1, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Honey", "honey", "Raw and processed honey", sweeteners.Id, 2, dietaryFlags: "vegetarian"),
                CreateSubCategory("Maple Syrup", "maple-syrup", "Pure maple syrup and products", sweeteners.Id, 3, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Molasses", "molasses", "Blackstrap and light molasses", sweeteners.Id, 4, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Date Syrup", "date-syrup", "Date-based sweeteners", sweeteners.Id, 5, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Artificial Sweeteners", "artificial-sweeteners", "Stevia, monk fruit, etc.", sweeteners.Id, 6, dietaryFlags: "vegan,vegetarian"),
            };
            await _db.IngredientCategories.AddRangeAsync(sweetSubs, ct);

            // ========== CONDIMENTS SUBCATEGORIES ==========
            var condSubs = new List<IngredientCategory>
            {
                CreateSubCategory("Vinegars", "vinegars", "Balsamic, apple cider, rice vinegar", condiments.Id, 1, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Soy Sauce & Asian Sauces", "soy-sauce-asian", "Soy sauce, fish sauce, oyster sauce", condiments.Id, 2, commonAllergens: "Soy,Fish,Shellfish"),
                CreateSubCategory("Hot Sauces", "hot-sauces", "Sriracha, Tabasco, harissa", condiments.Id, 3, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Mustards", "mustards", "Dijon, yellow, whole grain", condiments.Id, 4, commonAllergens: "Mustard", dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Tomato Products", "tomato-products", "Ketchup, tomato paste, marinara", condiments.Id, 5, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Mayonnaise & Aioli", "mayonnaise-aioli", "Regular and vegan mayo", condiments.Id, 6, commonAllergens: "Eggs"),
                CreateSubCategory("Stock & Broth", "stock-broth", "Chicken, beef, vegetable stock", condiments.Id, 7),
                CreateSubCategory("Tahini & Nut Butters", "tahini-nut-butters", "Tahini, peanut butter, almond butter", condiments.Id, 8, commonAllergens: "Sesame,Peanuts,Tree Nuts"),
            };
            await _db.IngredientCategories.AddRangeAsync(condSubs, ct);

            // ========== NUTS & SEEDS SUBCATEGORIES ==========
            var nutSubs = new List<IngredientCategory>
            {
                CreateSubCategory("Tree Nuts", "tree-nuts", "Almonds, walnuts, cashews, pistachios", nutsSeeds.Id, 1, commonAllergens: "Tree Nuts", dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Peanuts", "peanuts", "Peanuts and peanut products", nutsSeeds.Id, 2, commonAllergens: "Peanuts", dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Seeds", "seeds", "Sesame, sunflower, pumpkin seeds", nutsSeeds.Id, 3, commonAllergens: "Sesame", dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Chia & Flax", "chia-flax", "Chia seeds and flaxseed", nutsSeeds.Id, 4, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Pine Nuts", "pine-nuts", "Pine nuts/pignoli", nutsSeeds.Id, 5, commonAllergens: "Tree Nuts", dietaryFlags: "vegan,vegetarian"),
            };
            await _db.IngredientCategories.AddRangeAsync(nutSubs, ct);

            // ========== BEVERAGES SUBCATEGORIES ==========
            var bevSubs = new List<IngredientCategory>
            {
                CreateSubCategory("Tea", "tea", "Black, green, herbal teas", beverages.Id, 1, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Coffee", "coffee", "Ground coffee, espresso", beverages.Id, 2, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Juices", "juices", "Fruit and vegetable juices", beverages.Id, 3, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Coconut Water", "coconut-water", "Plain and flavored", beverages.Id, 4, dietaryFlags: "vegan,vegetarian"),
            };
            await _db.IngredientCategories.AddRangeAsync(bevSubs, ct);

            // ========== BAKING SUBCATEGORIES ==========
            var bakingSubs = new List<IngredientCategory>
            {
                CreateSubCategory("Flour", "flour", "All-purpose, bread, whole wheat", bakingEssentials.Id, 1, commonAllergens: "Gluten,Wheat"),
                CreateSubCategory("Gluten-Free Flour", "gluten-free-flour", "Almond, coconut, rice flour", bakingEssentials.Id, 2, commonAllergens: "Tree Nuts", dietaryFlags: "gluten-free"),
                CreateSubCategory("Leavening Agents", "leavening-agents", "Baking powder, baking soda, yeast", bakingEssentials.Id, 3, dietaryFlags: "vegan,vegetarian"),
                CreateSubCategory("Chocolate & Cocoa", "chocolate-cocoa", "Cocoa powder, chocolate chips", bakingEssentials.Id, 4, commonAllergens: "Milk"),
                CreateSubCategory("Vanilla & Extracts", "vanilla-extracts", "Vanilla extract, almond extract", bakingEssentials.Id, 5),
                CreateSubCategory("Cornstarch & Thickeners", "cornstarch-thickeners", "Cornstarch, arrowroot, tapioca", bakingEssentials.Id, 6, dietaryFlags: "gluten-free,vegan"),
            };
            await _db.IngredientCategories.AddRangeAsync(bakingSubs, ct);

            await _db.SaveChangesAsync(ct);

            var totalCount = await _db.IngredientCategories.CountAsync(ct);
            _logger.LogInformation("Seeded {Count} ingredient categories", totalCount);
        }

        private static IngredientCategory CreateCategory(
            string name,
            string slug,
            string description,
            int sortOrder,
            string? commonAllergens = null,
            string? dietaryFlags = null)
        {
            return new IngredientCategory
            {
                Name = name,
                Slug = slug,
                Description = description,
                SortOrder = sortOrder,
                CommonAllergens = commonAllergens,
                DietaryFlags = dietaryFlags,
                IsActive = true
            };
        }

        private static IngredientCategory CreateSubCategory(
            string name,
            string slug,
            string description,
            int parentId,
            int sortOrder,
            string? commonAllergens = null,
            string? dietaryFlags = null)
        {
            return new IngredientCategory
            {
                Name = name,
                Slug = slug,
                Description = description,
                ParentCategoryId = parentId,
                SortOrder = sortOrder,
                CommonAllergens = commonAllergens,
                DietaryFlags = dietaryFlags,
                IsActive = true
            };
        }
    }
}
