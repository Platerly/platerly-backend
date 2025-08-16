namespace Platerly.Api.Models
{
    public class GlobalIngredientAllergen
    {
        public Guid GlobalIngredientId { get; set; }
        public GlobalIngredient GlobalIngredient { get; set; } = null!;

        public string AllergenType { get; set; } = string.Empty;
    }

}
