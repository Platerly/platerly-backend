namespace Platerly.Api.Models
{
    public class IngredientAllergen
    {
        public Guid IngredientId { get; set; }
        public Ingredient Ingredient { get; set; } = null!;

        public string AllergenType { get; set; } = string.Empty;
    }

}
