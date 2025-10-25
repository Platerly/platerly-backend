using System.Linq;
using System.Collections.Generic;
using Platerly.Api.Models;

namespace Platerly.Api
{
    public static class TagMappingExtensions
    {
        public static List<string> ToTagNames(this Recipe recipe)
            => recipe?.RecipeTags?
                   .Select(rt => rt.Tag.Name)
                   .Where(n => !string.IsNullOrWhiteSpace(n))
                   .Distinct()
                   .ToList()
               ?? new List<string>();

        public static void ReplaceTagsWithNames(this Recipe recipe, IEnumerable<string> tagNames, IReadOnlyDictionary<string, int> tagLookupByName)
        {
            if (recipe == null) return;

            var names = (tagNames ?? Enumerable.Empty<string>())
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Select(n => n.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

            recipe.RecipeTags ??= new List<RecipeTag>();
            recipe.RecipeTags.Clear();

            foreach (var name in names)
            {
                if (tagLookupByName.TryGetValue(name, out var tagId))
                {
                    recipe.RecipeTags.Add(new RecipeTag {TagId = tagId });
                }
            }
        }
    }
}
