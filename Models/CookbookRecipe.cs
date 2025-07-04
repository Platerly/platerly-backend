using System;

namespace Platerly.Api.Models
{
    public class CookbookRecipe
    {
        public Guid CookbookId { get; set; }
        public Cookbook Cookbook { get; set; } = null!;

        public Guid RecipeId { get; set; }
        public Recipe Recipe { get; set; } = null!;
    }
}
