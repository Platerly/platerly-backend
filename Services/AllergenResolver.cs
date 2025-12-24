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
                // ======================= MILK / DAIRY / LACTOSE ===========================
                ["milk"] = new (string, Severity)[]
                {
                    ("milk", Severity.Medium),
                    ("dairy", Severity.Medium),
                    ("lactose", Severity.Medium),
                    ("yogurt", Severity.Medium),
                    ("greek yogurt", Severity.Medium),
                    ("cheese", Severity.Medium),
                    ("cheddar", Severity.Medium),
                    ("mozzarella", Severity.Medium),
                    ("butter", Severity.Medium),
                    ("cream", Severity.Medium),
                    ("whey", Severity.Medium),
                    ("casein", Severity.Medium),
                    ("milk powder", Severity.Medium),
                    ("skim milk", Severity.Medium),
                    ("evaporated milk", Severity.Medium),
                    ("condensed milk", Severity.Medium),
                },
                ["dairy"] = new (string, Severity)[] { ("milk", Severity.Medium), ("yogurt", Severity.Medium), ("cheese", Severity.Medium), ("butter", Severity.Medium), ("cream", Severity.Medium), ("whey", Severity.Medium), ("casein", Severity.Medium) },
                ["lactose"] = new (string, Severity)[] { ("milk", Severity.Medium), ("yogurt", Severity.Medium), ("cheese", Severity.Medium), ("whey", Severity.Medium), ("milk powder", Severity.Medium) },

                // ======================= GLUTEN / WHEAT / CELIAC ==========================
                ["gluten"] = new (string, Severity)[]
                {
                    ("wheat", Severity.High),
                    ("barley", Severity.High),
                    ("rye", Severity.High),
                    ("malt", Severity.High),
                    ("breadcrumbs", Severity.High),
                    ("bread", Severity.High),
                    ("flour", Severity.High),
                    ("wheat flour", Severity.High),
                    ("semolina", Severity.High),
                    ("couscous", Severity.High),
                    ("pasta", Severity.High),
                    ("soy sauce", Severity.Medium), // often contains wheat (not always) -> medium
                },
                ["wheat"] = new (string, Severity)[] 
                { 
                    ("wheat", Severity.High), ("flour", Severity.High), 
                    ("bread", Severity.High), ("pasta", Severity.High), 
                    ("semolina", Severity.High) },
                ["celiac"] = new (string, Severity)[] 
                { 
                    ("gluten", Severity.High), 
                    ("wheat", Severity.High), 
                    ("barley", Severity.High), 
                    ("rye", Severity.High), 
                    ("malt", Severity.High) },

                // ======================= EGGS ============================================
                ["egg"] = new (string, Severity)[]
                {
                    ("egg", Severity.High),
                    ("eggs", Severity.High),
                    ("egg white", Severity.High),
                    ("egg yolk", Severity.High),
                    ("albumin", Severity.Medium),
                    ("mayonnaise", Severity.Medium),
                },
                // ======================= PEANUTS / TREE NUTS ==============================
                ["peanut"] = new (string, Severity)[]
                {
                    ("peanut", Severity.High),
                    ("peanuts", Severity.High),
                    ("peanut butter", Severity.High),
                    ("groundnut", Severity.High),
                },
                ["tree nut"] = new (string, Severity)[]
                {
                    ("almond", Severity.High),
                    ("cashew", Severity.High),
                    ("walnut", Severity.High),
                    ("hazelnut", Severity.High),
                    ("pistachio", Severity.High),
                    ("pecan", Severity.High),
                    ("macadamia", Severity.High),
                    ("brazil nut", Severity.High),
                },
                // ======================= SESAME / SOY ====================================
                ["sesame"] = new (string, Severity)[]
                {
                    ("sesame", Severity.High),
                    ("tahini", Severity.High),
                },
                ["soy"] = new (string, Severity)[]
                {
                    ("soy", Severity.High),
                    ("soya", Severity.High),
                    ("soybean", Severity.High),
                    ("tofu", Severity.High),
                    ("tempeh", Severity.High),
                    ("soy sauce", Severity.Medium),
                    ("edamame", Severity.High),
                },
                // ======================= SULFITES =========================================
                ["sulfites"] = new (string, Severity)[]
                {
                    ("sulfite", Severity.High),
                    ("sulfites", Severity.High),
                    ("sulphite", Severity.High),
                    ("sulphites", Severity.High),
                    ("sulfur dioxide", Severity.High),
                    ("wine", Severity.Medium),
                    ("vinegar", Severity.Low), // sometimes
                    ("dried fruit", Severity.Medium),
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
