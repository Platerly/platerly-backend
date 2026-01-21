using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Platerly.Api.Helpers
{
    public static class SlugHelper
    {
        /// <summary>
        /// Generates a URL-friendly slug from a title.
        /// Example: "Mom's Chocolate Chip Cookies!" -> "moms-chocolate-chip-cookies"
        /// </summary>
        public static string GenerateSlug(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return "recipe";

            // Normalize unicode and remove diacritics (é -> e, ñ -> n)
            var normalized = title.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (var c in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }

            var slug = sb.ToString().Normalize(NormalizationForm.FormC);

            // Convert to lowercase
            slug = slug.ToLowerInvariant();

            // Replace spaces and underscores with hyphens
            slug = Regex.Replace(slug, @"[\s_]+", "-");

            // Remove all non-alphanumeric characters except hyphens
            slug = Regex.Replace(slug, @"[^a-z0-9\-]", "");

            // Collapse multiple hyphens into one
            slug = Regex.Replace(slug, @"-+", "-");

            // Trim hyphens from start and end
            slug = slug.Trim('-');

            // Ensure minimum length
            if (string.IsNullOrEmpty(slug))
                return "recipe";

            // Limit length (leave room for potential -2, -3 suffixes)
            if (slug.Length > 180)
                slug = slug.Substring(0, 180).TrimEnd('-');

            return slug;
        }

        /// <summary>
        /// Generates a unique slug by appending -2, -3, etc. if the base slug exists.
        /// </summary>
        public static string MakeUnique(string baseSlug, Func<string, bool> slugExists)
        {
            if (!slugExists(baseSlug))
                return baseSlug;

            var counter = 2;
            string candidate;

            do
            {
                candidate = $"{baseSlug}-{counter}";
                counter++;
            }
            while (slugExists(candidate) && counter < 1000);

            return candidate;
        }
    }
}
