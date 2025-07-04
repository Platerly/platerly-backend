using Platerly.Api.Data;
using Platerly.Api.Models;

namespace Platerly.Api.Seeding
{
    public static class AllergenSeeder
    {
        public static async Task SeedIngredientAllergensAsync(PlaterlyDbContext context)
        {
            var allergenMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "Egg", new List<string> { "Egg" } },
                { "Milk", new List<string> { "Milk", "Lactose" } },
                { "Soy", new List<string> { "Soy" } },
                { "Wheat", new List<string> { "Gluten", "Wheat" } },
                { "Peanut", new List<string> { "Peanut", "Legume" } },
                { "Almond", new List<string> { "TreeNut", "Almond" } },
                { "Cashew", new List<string> { "TreeNut", "Cashew" } },
                { "Fish", new List<string> { "Fish" } },
                { "Shellfish", new List<string> { "Shellfish" } },
                { "Chocolate", new List<string> { "Nickel" } },
                { "Beans", new List<string> { "Nickel", "Legume" } },
                { "Oats", new List<string> { "Nickel" } },
                { "Lentils", new List<string> { "Nickel", "Legume" } },
                { "Soy Sauce", new List<string> { "Soy", "Gluten" } }
            };

            var ingredients = context.Ingredients.ToList();

            foreach (var ingredient in ingredients)
            {
                foreach (var entry in allergenMap)
                {
                    if (entry.Key.Contains(ingredient.Name, StringComparison.OrdinalIgnoreCase) ||
                        ingredient.Name.Contains(entry.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        ingredient.IsAllergenic = true;
                        ingredient.AllergenTypes = entry.Value;
                        break; // apply the first match only
                    }
                }
            }

            await context.SaveChangesAsync();
        }
    }
}
