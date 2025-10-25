using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Platerly.Api.Services
{
    public class AllergenResolver : IAllergenResolver
    {
        public enum Severity { Low = 0, Medium = 1, High = 2 }

        // --------------------------------------------------------------------
        // CENTRAL ALLERGEN MAP (term → ingredients with severity)
        // --------------------------------------------------------------------
        private static readonly Dictionary<string, (string term, Severity severity)[]> Map =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // ======================= NICKEL ===========================
                ["nickel"] = new (string, Severity)[]
                {
                    // ===== HIGH =====
                    ("chocolate", Severity.High), ("cocoa", Severity.High), ("cacao", Severity.High), ("dark chocolate", Severity.High),

                    ("soy", Severity.High), ("soya", Severity.High),
                    ("soybean", Severity.High), ("soybeans", Severity.High),
                    ("tofu", Severity.High), ("tempeh", Severity.High),
                    ("soy milk", Severity.High), ("soy flour", Severity.High),
                    ("soy protein", Severity.High), ("soy sauce", Severity.High),

                    ("lentil", Severity.High), ("lentils", Severity.High),
                    ("red lentils", Severity.High), ("green lentils", Severity.High),
                    ("kidney bean", Severity.High), ("kidney beans", Severity.High),
                    ("black bean", Severity.High), ("black beans", Severity.High),
                    ("pinto bean", Severity.High), ("pinto beans", Severity.High),
                    ("navy bean", Severity.High), ("navy beans", Severity.High),
                    ("broad bean", Severity.High), ("broad beans", Severity.High),
                    ("chickpea", Severity.High), ("chickpeas", Severity.High),
                    ("garbanzo bean", Severity.High), ("garbanzo beans", Severity.High),
                    ("split pea", Severity.High), ("split peas", Severity.High),
                    ("black-eyed pea", Severity.High), ("black-eyed peas", Severity.High),
                    ("mung bean", Severity.High), ("mung beans", Severity.High),

                    ("peanut", Severity.High), ("peanuts", Severity.High),
                    ("almond", Severity.High), ("almonds", Severity.High),
                    ("hazelnut", Severity.High), ("hazelnuts", Severity.High),
                    ("walnut", Severity.High), ("walnuts", Severity.High),
                    ("cashew", Severity.High), ("cashews", Severity.High),
                    ("pistachio", Severity.High), ("pistachios", Severity.High),

                    ("sunflower seed", Severity.High), ("sunflower seeds", Severity.High),
                    ("sesame seed", Severity.High), ("sesame seeds", Severity.High),
                    ("pumpkin seed", Severity.High), ("pumpkin seeds", Severity.High),
                    ("flaxseed", Severity.High), ("linseed", Severity.High),

                    ("whole wheat", Severity.High), ("buckwheat", Severity.High),
                    ("rye", Severity.High), ("oat", Severity.High), ("oats", Severity.High),
                    ("millet", Severity.High), ("barley", Severity.High), ("bran", Severity.High),
                    ("whole grain flour", Severity.High), ("multigrain bread", Severity.High),

                    ("canned bean", Severity.High), ("canned beans", Severity.High),
                    ("canned pea", Severity.High), ("canned peas", Severity.High),
                    ("canned vegetable", Severity.High), ("canned vegetables", Severity.High),
                    ("tinned tomato", Severity.High), ("tinned tomatoes", Severity.High),

                    ("tuna", Severity.High), ("mackerel", Severity.High), ("salmon", Severity.High),
                    ("herring", Severity.High), ("shellfish", Severity.High),

                    ("baking powder", Severity.High), ("gelatin", Severity.High),
                    ("licorice", Severity.High), ("margarine", Severity.High),

                    // ===== MEDIUM =====
                    ("spinach", Severity.Medium), ("kale", Severity.Medium),
                    ("broccoli", Severity.Medium), ("cabbage", Severity.Medium),
                    ("lettuce", Severity.Medium), ("asparagus", Severity.Medium),
                    ("green beans", Severity.Medium), ("brussels sprouts", Severity.Medium),
                    ("cauliflower", Severity.Medium),
                    ("pear", Severity.Medium), ("apple", Severity.Medium), ("banana", Severity.Medium),
                    ("pineapple", Severity.Medium), ("fig", Severity.Medium), ("prune", Severity.Medium),
                    ("brown rice", Severity.Medium), ("rice bran", Severity.Medium),
                    ("cornmeal", Severity.Medium), ("cornflakes", Severity.Medium),
                    ("quinoa", Severity.Medium), ("whole grain pasta", Severity.Medium),
                    ("whole grain bread", Severity.Medium),
                    ("beef", Severity.Medium), ("chicken", Severity.Medium),
                    ("duck", Severity.Medium), ("cod", Severity.Medium),
                    ("sardine", Severity.Medium), ("tea", Severity.Medium),
                    ("coffee", Severity.Medium),

                    // ===== LOW =====
                    ("white rice", Severity.Low), ("white bread", Severity.Low),
                    ("refined wheat flour", Severity.Low), ("semolina", Severity.Low),
                    ("corn", Severity.Low),
                    ("milk", Severity.Low), ("cream", Severity.Low), ("butter", Severity.Low),
                    ("cheese", Severity.Low), ("yogurt", Severity.Low),
                    ("lamb", Severity.Low), ("veal", Severity.Low),
                    ("turkey", Severity.Low), ("chicken breast", Severity.Low), ("egg", Severity.Low),
                    ("grape", Severity.Low), ("melon", Severity.Low), ("watermelon", Severity.Low),
                    ("mango", Severity.Low), ("orange", Severity.Low), ("strawberry", Severity.Low),
                    ("potato", Severity.Low), ("carrot", Severity.Low), ("cucumber", Severity.Low),
                    ("zucchini", Severity.Low), ("bell pepper", Severity.Low),
                    ("pumpkin", Severity.Low), ("onion", Severity.Low),
                    ("herbal tea", Severity.Low), ("water", Severity.Low),
                    ("apple juice", Severity.Low),
                    ("olive oil", Severity.Low), ("canola oil", Severity.Low),
                    ("sunflower oil", Severity.Low), ("ghee", Severity.Low),
                    ("sugar", Severity.Low), ("honey", Severity.Low),
                    ("salt", Severity.Low), ("vinegar", Severity.Low)
                },

            };

        // --------------------------------------------------------------------
        // UTILITIES
        // --------------------------------------------------------------------
        private static string Normalize(string s) =>
            Regex.Replace((s ?? "").Trim().ToLowerInvariant(), @"\s+", " ");

        // --------------------------------------------------------------------
        // EXPANSION LOGIC
        // --------------------------------------------------------------------

        public HashSet<string> ExpandKeywords(IEnumerable<string> rawKeywords)
        {
            return ExpandWithSeverity(rawKeywords)
                .Select(x => x.term)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public List<(string term, Severity severity)> ExpandWithSeverity(IEnumerable<string> rawKeywords)
        {
            var results = new Dictionary<string, Severity>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in rawKeywords ?? Enumerable.Empty<string>())
            {
                var key = Normalize(raw);
                if (string.IsNullOrWhiteSpace(key)) continue;

                if (!results.ContainsKey(key))
                    results[key] = Severity.Low;

                if (Map.TryGetValue(key, out var direct))
                {
                    foreach (var (term, sev) in direct)
                        if (!results.ContainsKey(term) || results[term] < sev)
                            results[term] = sev;
                }

                foreach (var kv in Map)
                {
                    if (key.Contains(kv.Key) || kv.Key.Contains(key))
                    {
                        foreach (var (term, sev) in kv.Value)
                            if (!results.ContainsKey(term) || results[term] < sev)
                                results[term] = sev;
                    }
                }
            }

            return results.Select(x => (x.Key, x.Value)).ToList();
        }

        public List<string> FilterByMinSeverity(IEnumerable<(string term, Severity severity)> terms, string minSeverity)
        {
            var min = minSeverity?.ToLowerInvariant() switch
            {
                "high" => Severity.High,
                "medium" => Severity.Medium,
                _ => Severity.Low
            };

            return terms
                .Where(t => t.severity >= min)
                .Select(t => t.term)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
