namespace Platerly.Api.Models
{
    public class GlobalIngredient
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty;

        // Optional: future use cases like smart tagging
        public bool IsAllergenic { get; set; } = false;
        public List<GlobalIngredientAllergen> Allergens { get; set; } = new();


        public List<Ingredient> Ingredients { get; set; } = new();
    }
}
