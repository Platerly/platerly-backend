using System;
using System.Collections.Generic;

namespace Platerly.Api.Services.Allergy
{
    /// <summary>
    /// Centralized normalization logic for allergen names.
    /// Converts raw strings from DB (AllergenType) or user settings
    /// into canonical keys and consistent display labels.
    /// 
    /// Key()      → normalized, lower-case internal lookup key
    /// Canonical → stable, pretty-cased canonical name (“Sulfites”)
    /// Display()  → converts a normalized key back to a UI-friendly label
    /// </summary>
    public static class AllergenNameNormalizer
    {
        // 🔹 Map variants / synonyms → a canonical name
        private static readonly Dictionary<string, string> Canon =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // -----------------------
                // Sulfites
                // -----------------------
                ["sulfite"] = "Sulfites",
                ["sulfites"] = "Sulfites",
                ["sulphite"] = "Sulfites",
                ["sulphites"] = "Sulfites",
                ["sulfur dioxide"] = "Sulfites",
                ["so2"] = "Sulfites",

                // -----------------------
                // Nickel
                // -----------------------
                ["nickel"] = "Nickel",

                // -----------------------
                // Milk / Dairy family
                // -----------------------
                ["milk"] = "Milk",
                ["dairy"] = "Milk",
                ["lactose"] = "Milk",
                ["casein"] = "Milk",
                ["whey"] = "Milk",

                // -----------------------
                // Gluten
                // -----------------------
                ["gluten"] = "Gluten",
                ["wheat"] = "Gluten",
                ["barley"] = "Gluten",
                ["rye"] = "Gluten",
                ["triticale"] = "Gluten",
                ["spelt"] = "Gluten",
                ["semolina"] = "Gluten",
                ["malt"] = "Gluten"
            };

        /// <summary>
        /// Returns a canonical allergen name (e.g. “Sulfites”, “Milk”).
        /// Unknown values are capitalized normally.
        /// </summary>
        public static string Canonical(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var trimmed = value.Trim();

            // If it's a known variant → mapped canonical
            if (Canon.TryGetValue(trimmed, out var mapped))
                return mapped;

            // Default canonicalization → capitalized
            return char.ToUpperInvariant(trimmed[0]) +
                   (trimmed.Length > 1 ? trimmed[1..] : string.Empty);
        }

        /// <summary>
        /// Returns a normalized, lower-cased key used for internal
        /// dictionary lookups (e.g. “sulfites”, “milk”).
        /// </summary>
        public static string Key(string? value)
            => Canonical(value).ToLowerInvariant();

        /// <summary>
        /// Converts a normalized key (“sulfites”) back into a display label (“Sulfites”).
        /// </summary>
        public static string Display(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            // Key is already lower-case — find a canonical name that matches
            // our canonical value when normalized.
            foreach (var kvp in Canon)
            {
                var canon = Canonical(kvp.Key);
                if (canon.Equals(key, StringComparison.OrdinalIgnoreCase))
                    return canon;
            }

            // Default: capitalized key
            return char.ToUpperInvariant(key[0]) +
                   (key.Length > 1 ? key[1..] : string.Empty);
        }
    }
}
