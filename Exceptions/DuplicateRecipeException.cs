namespace Platerly.Api.Exceptions
{
    public class DuplicateRecipeException : Exception
    {
        public Guid RecipeId { get; }

        public DuplicateRecipeException(string message, Guid recipeId) : base(message)
        {
            RecipeId = recipeId;
        }
    }
}
