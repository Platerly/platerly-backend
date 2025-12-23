using System;
using System.Collections.Generic;

namespace Platerly.Api.Services.Allergy
{
    /// <summary>
    /// Centralized normalization logic for allergen names.
    /// Converts raw strings from DB (AllergenType) or user settings
    /// into canonical keys and consistent display labels.
    ///
    /// Canonical names you want product-wide:
    /// - Milk
    /// - Gluten
    /// - Eggs, Fish, Shellfish, Peanuts, Tree Nuts, Sesame, Soy, Wheat, Sulfites, Nickel
    /// </summary>
    public static class AllergenNameNormalizer
    {
        // Map variants/synonyms/categories -> canonical display name
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
                ["sulphur dioxide"] = "Sulfites",

                // -----------------------
                // Nickel
                // -----------------------
                ["nickel"] = "Nickel",

                // -----------------------
                // Milk / Dairy
                // -----------------------
                ["milk"] = "Milk",
                ["dairy"] = "Milk",
                ["dairy_lactose"] = "Milk",          // ✅ category -> canonical
                ["lactose"] = "Milk",
                ["casein"] = "Milk",
                ["whey"] = "Milk",

                // -----------------------
                // Gluten / Celiac
                // -----------------------
                ["gluten"] = "Gluten",
                ["gluten_celiac"] = "Gluten",        // ✅ category -> canonical
                ["wheat"] = "Gluten",                // NOTE: wheat is also a standalone allergy; canonicalizing to Gluten
                ["barley"] = "Gluten",
                ["rye"] = "Gluten",
                ["triticale"] = "Gluten",
                ["spelt"] = "Gluten",
                ["semolina"] = "Gluten",
                ["malt"] = "Gluten",

                // -----------------------
                // Major single allergens
                // -----------------------
                ["eggs"] = "Eggs",
                ["egg"] = "Eggs",
                ["fish"] = "Fish",
                ["shellfish"] = "Shellfish",
                ["peanut"] = "Peanuts",
                ["peanuts"] = "Peanuts",
                ["tree nuts"] = "Tree Nuts",
                ["treenuts"] = "Tree Nuts",
                ["sesame"] = "Sesame",
                ["soy"] = "Soy",
                ["soya"] = "Soy",

                // Some systems have these as explicit
                ["wheat_allergy"] = "Wheat",
                ["wheat (allergy)"] = "Wheat",
            };

        /// <summary>
        /// Returns a canonical allergen display name (e.g. “Sulfites”, “Milk”).
        /// Unknown values are title-cased.
        /// </summary>
        public static string Canonical(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var trimmed = value.Trim();

            if (Canon.TryGetValue(trimmed, out var mapped))
                return mapped;

            // Default title-ish case
            return char.ToUpperInvariant(trimmed[0]) +
                   (trimmed.Length > 1 ? trimmed[1..] : string.Empty);
        }

        /// <summary>
        /// Returns a normalized key used for internal lookups (lower-case).
        /// Example: "Milk" -> "milk", "Sulfites" -> "sulfites"
        /// </summary>
        public static string Key(string? value)
            => Canonical(value).ToLowerInvariant();

        /// <summary>
        /// Converts a normalized key back into a display label.
        /// Example: "milk" -> "Milk"
        /// </summary>
        public static string Display(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            // If key matches a canonical name, return it properly cased
            // (Milk, Gluten, Eggs, etc.)
            var canonical = Canonical(key);
            if (!string.IsNullOrWhiteSpace(canonical))
                return canonical;

            return char.ToUpperInvariant(key[0]) + (key.Length > 1 ? key[1..] : string.Empty);
        }
    }
}
