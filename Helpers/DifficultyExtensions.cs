using System;
using Platerly.Api.Models;

namespace Platerly.Api.Helpers
{
    public static class DifficultyExtensions
    {
        public static Difficulty? ParseDifficultyOrNull(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            return Enum.TryParse<Difficulty>(input.Trim(), ignoreCase: true, out var val) ? val : null;
        }

        public static string ToDisplayString(this Difficulty? value)
            => value?.ToString() ?? string.Empty;

        public static RecipeType? ParseRecipeTypeOrNull(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            return Enum.TryParse<RecipeType>(input.Trim(), ignoreCase: true, out var val) ? val : null;
        }

        public static string ToDisplayString(this RecipeType value)
            => value.ToString();
    }
}
