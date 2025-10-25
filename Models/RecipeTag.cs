namespace Platerly.Api.Models
{
    public class RecipeTag
    {
        public Guid RecipeId { get; set; }
        public Recipe Recipe { get; set; } = default!;
        public int TagId { get; set; }
        public Tag Tag { get; set; } = default!;
    }
}
