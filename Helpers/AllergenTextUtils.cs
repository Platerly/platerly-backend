namespace Platerly.Api.Helpers
{
    /// <summary>
    /// Shared text utilities for allergen matching. Used by AllergyAlertService, FeedController,
    /// and RecipeController so that normalisation and severity ranking are consistent everywhere.
    /// </summary>
    public static class AllergenTextUtils
    {
        /// <summary>
        /// Normalises an ingredient or allergen term to a lowercase, whitespace-collapsed string.
        /// </summary>
        public static string NormKey(string s)
            => string.Join(" ",
                (s ?? "")
                    .Trim()
                    .ToLowerInvariant()
                    .Replace("\t", " ")
                    .Replace("\n", " ")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            );

        /// <summary>
        /// Returns a numeric rank for a severity string.
        /// Unknown/null values rank as 0 (below Low=1) so a named severity always wins.
        /// </summary>
        public static int Rank(string? s)
            => s?.ToLowerInvariant() switch
            {
                "high" => 3,
                "medium" => 2,
                "low" => 1,
                _ => 0
            };

        /// <summary>
        /// Returns whichever of <paramref name="a"/> or <paramref name="b"/> has the higher severity rank.
        /// </summary>
        public static string BestSeverity(string a, string b)
            => Rank(a) >= Rank(b) ? a : b;

        /// <summary>
        /// Result type for <see cref="ComputeHitsFromKeywords"/>.
        /// </summary>
        public sealed class AllergyHit
        {
            public int IngredientIndex { get; init; }
            public string IngredientName { get; init; } = "";
            public string MatchedKeyword { get; init; } = "";
        }

        /// <summary>
        /// Performs a simple substring keyword match against a list of ingredient names.
        /// Returns one hit per ingredient (first matching keyword wins).
        /// </summary>
        public static List<AllergyHit> ComputeHitsFromKeywords(
            IReadOnlyList<string> keywords,
            List<string> ingredientNames)
        {
            var hits = new List<AllergyHit>();
            if (keywords == null || keywords.Count == 0 || ingredientNames.Count == 0) return hits;

            var keys = keywords
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim().ToLowerInvariant())
                .Distinct()
                .ToArray();

            for (int i = 0; i < ingredientNames.Count; i++)
            {
                var name = ingredientNames[i];
                if (string.IsNullOrWhiteSpace(name)) continue;

                var lower = name.ToLowerInvariant();
                foreach (var kw in keys)
                {
                    if (lower.Contains(kw))
                    {
                        hits.Add(new AllergyHit
                        {
                            IngredientIndex = i,
                            IngredientName = name,
                            MatchedKeyword = kw
                        });
                        break;
                    }
                }
            }
            return hits;
        }
    }
}
