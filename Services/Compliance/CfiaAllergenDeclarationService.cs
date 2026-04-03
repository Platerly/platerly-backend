using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Compliance
{
    /// <summary>
    /// Generates CFIA-compliant bilingual allergen declaration text for Canadian food packaging.
    ///
    /// Detection pipeline:
    ///   1. Match submitted ingredient names against GlobalIngredients.NormalizedName
    ///      (with alias fallback) and load their GlobalIngredientAllergen rows.
    ///   2. For ingredients with no structured allergen record, scan the ingredient name
    ///      against the AllergenKeyword lexicon (keyword text contained in ingredient name).
    ///   3. Aggregate detected allergens into the 11 CFIA priority allergen categories,
    ///      splitting into "Contains" (definite) vs "May Contain" (cross-contact / may-contain).
    ///   4. Format bilingual declaration text per SFCR s.257 conventions.
    ///
    /// CFIA 11 Priority Allergens (Safe Food for Canadians Regulations):
    ///   Peanuts, Tree Nuts, Sesame Seeds, Milk, Eggs, Fish, Shellfish,
    ///   Wheat and triticale (Gluten), Soy, Mustard, Sulphites.
    /// </summary>
    public class CfiaAllergenDeclarationService : ICfiaAllergenDeclarationService
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<CfiaAllergenDeclarationService> _logger;

        // Maps internal AllergenType → (CFIA English label, CFIA French label)
        // Order matches conventional CFIA listing sequence.
        private static readonly (string Type, string English, string French)[] CfiaAllergens =
        {
            ("Peanuts",    "Peanuts",               "Arachides"),
            ("Tree Nuts",  "Tree nuts",              "Noix"),
            ("Sesame",     "Sesame seeds",           "Graines de sésame"),
            ("Milk",       "Milk",                   "Lait"),
            ("Eggs",       "Eggs",                   "Œufs"),
            ("Fish",       "Fish",                   "Poissons"),
            ("Shellfish",  "Shellfish",              "Crustacés et mollusques"),
            ("Gluten",     "Wheat and triticale",    "Blé et triticale"),
            ("Soy",        "Soy",                    "Soya"),
            ("Mustard",    "Mustard",                "Moutarde"),
            ("Sulfites",   "Sulphites",              "Sulfites"),
        };

        private static readonly HashSet<string> CfiaAllergenTypes =
            new(CfiaAllergens.Select(a => a.Type), StringComparer.OrdinalIgnoreCase);

        public CfiaAllergenDeclarationService(
            PlaterlyDbContext db,
            ILogger<CfiaAllergenDeclarationService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Generates a bilingual CFIA allergen declaration for a list of ingredient names.
        ///
        /// Detection pipeline:
        /// <list type="number">
        ///   <item>Bulk-fetch <c>GlobalIngredient</c> rows matching submitted normalised names.</item>
        ///   <item>Alias fallback for unmatched names — scans all active ingredients' comma-separated
        ///     <c>Aliases</c> field for a case-insensitive match.</item>
        ///   <item>Keyword lexicon fallback — for still-unmatched names, scans the
        ///     <c>AllergenKeywords</c> table; if a keyword text is contained in the ingredient name,
        ///     its linked CFIA allergen types are added as definite ("Contains") allergens.</item>
        ///   <item>Aggregate into "Contains" vs "May Contain" maps by <c>AllergenType</c>, ordered
        ///     in the conventional CFIA listing sequence.</item>
        ///   <item>Format bilingual text per SFCR s.257 conventions, e.g.
        ///     "Contains: Peanuts, Milk. / Contient\u00a0: Arachides, Lait."</item>
        /// </list>
        /// Only the 11 CFIA priority allergen types (as defined in <c>CfiaAllergens</c>) are
        /// included in the declaration output; other allergen types are silently excluded.
        /// </summary>
        /// <param name="request">Ingredient name list and optional allergen-type filter.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Full declaration result with bilingual text, per-ingredient breakdown, and unmatched names.</returns>
        public async Task<CfiaAllergenDeclarationResult> GenerateDeclarationAsync(
            CfiaAllergenDeclarationRequest request,
            CancellationToken ct = default)
        {
            var submittedNames = request.Ingredients
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var normalizedNames = submittedNames
                .Select(n => n.ToLower())
                .ToList();

            // ── 1. Bulk-fetch structured allergen data ──────────────────────────

            var structuredData = await _db.GlobalIngredients
                .AsNoTracking()
                .Include(gi => gi.Allergens)
                .Where(gi => gi.IsActive && normalizedNames.Contains(gi.NormalizedName))
                .ToListAsync(ct);

            // Build lookup: normalized name → structured ingredient
            var structuredLookup = structuredData
                .ToDictionary(gi => gi.NormalizedName, StringComparer.OrdinalIgnoreCase);

            // ── 2. Alias fallback for unmatched names ───────────────────────────

            var unmatchedNormalized = normalizedNames
                .Where(n => !structuredLookup.ContainsKey(n))
                .ToList();

            if (unmatchedNormalized.Any())
            {
                var aliasCandidates = await _db.GlobalIngredients
                    .AsNoTracking()
                    .Include(gi => gi.Allergens)
                    .Where(gi => gi.IsActive && gi.Aliases != null)
                    .ToListAsync(ct);

                foreach (var candidate in aliasCandidates)
                {
                    if (candidate.Aliases == null) continue;
                    var aliases = candidate.Aliases
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(a => a.ToLower());

                    foreach (var alias in aliases)
                    {
                        if (unmatchedNormalized.Contains(alias) &&
                            !structuredLookup.ContainsKey(alias))
                        {
                            structuredLookup[alias] = candidate;
                        }
                    }
                }
            }

            // Names still unmatched after alias fallback
            var noStructuredMatch = normalizedNames
                .Where(n => !structuredLookup.ContainsKey(n))
                .ToList();

            // ── 3. Keyword lexicon fallback ─────────────────────────────────────
            // For each unmatched ingredient, check whether the ingredient name contains
            // any keyword from the AllergenKeyword lexicon (e.g., "cream cheese" → "milk").

            // key: normalizedIngredientName, value: list of allergy names matched
            var keywordMatches = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            if (noStructuredMatch.Any())
            {
                // Load keywords and their allergy associations in one query
                var keywordRows = await _db.AllergenKeywords
                    .AsNoTracking()
                    .Include(k => k.Allergies)
                        .ThenInclude(ka => ka.Allergy)
                    .ToListAsync(ct);

                foreach (var ingNorm in noStructuredMatch)
                {
                    var matched = new List<string>();
                    foreach (var kw in keywordRows)
                    {
                        // The keyword text is stored as the actual word/phrase to look for
                        if (ingNorm.Contains(kw.Text.ToLower()))
                        {
                            foreach (var ka in kw.Allergies)
                            {
                                var allergyName = ka.Allergy.Name;
                                // Only include CFIA priority allergens
                                if (CfiaAllergenTypes.Contains(allergyName) &&
                                    !matched.Contains(allergyName, StringComparer.OrdinalIgnoreCase))
                                {
                                    matched.Add(allergyName);
                                }
                            }
                        }
                    }
                    if (matched.Any())
                        keywordMatches[ingNorm] = matched;
                }
            }

            // ── 4. Build per-ingredient breakdown ──────────────────────────────

            var breakdown = new List<CfiaIngredientBreakdown>();

            // Tracking sets for aggregated declaration (allergenType → set of source ingredient names)
            var containsMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var mayContainMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            int structuredMatchCount = 0;
            int keywordMatchCount = 0;
            var unmatchedNames = new List<string>();

            foreach (var ingName in submittedNames)
            {
                var norm = ingName.ToLower();
                var entry = new CfiaIngredientBreakdown { IngredientName = ingName };

                if (structuredLookup.TryGetValue(norm, out var gi))
                {
                    entry.DetectionMethod = "structured";
                    structuredMatchCount++;

                    foreach (var allergen in gi.Allergens)
                    {
                        // Only map to the 11 CFIA priority allergen types
                        if (!CfiaAllergenTypes.Contains(allergen.AllergenType))
                            continue;

                        bool isMayContain = allergen.MayContain || allergen.CrossContactRisk;

                        if (isMayContain)
                        {
                            entry.MayContainAllergens.Add(allergen.AllergenType);
                            if (!mayContainMap.ContainsKey(allergen.AllergenType))
                                mayContainMap[allergen.AllergenType] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            mayContainMap[allergen.AllergenType].Add(ingName);
                        }
                        else
                        {
                            entry.ContainsAllergens.Add(allergen.AllergenType);
                            if (!containsMap.ContainsKey(allergen.AllergenType))
                                containsMap[allergen.AllergenType] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            containsMap[allergen.AllergenType].Add(ingName);
                        }
                    }

                    // If the ingredient is allergenic but has no structured rows, use keyword as backup
                    if (!entry.ContainsAllergens.Any() && !entry.MayContainAllergens.Any() &&
                        keywordMatches.TryGetValue(norm, out var kwFallback))
                    {
                        foreach (var allergyName in kwFallback)
                        {
                            entry.ContainsAllergens.Add(allergyName);
                            if (!containsMap.ContainsKey(allergyName))
                                containsMap[allergyName] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            containsMap[allergyName].Add(ingName);
                        }
                        entry.DetectionMethod = "keyword";
                        structuredMatchCount--;
                        keywordMatchCount++;
                    }
                }
                else if (keywordMatches.TryGetValue(norm, out var kwResults))
                {
                    entry.DetectionMethod = "keyword";
                    keywordMatchCount++;

                    foreach (var allergyName in kwResults)
                    {
                        entry.ContainsAllergens.Add(allergyName);
                        if (!containsMap.ContainsKey(allergyName))
                            containsMap[allergyName] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        containsMap[allergyName].Add(ingName);
                    }
                }
                else
                {
                    entry.DetectionMethod = "unmatched";
                    unmatchedNames.Add(ingName);
                }

                breakdown.Add(entry);
            }

            // ── 5. Build ordered CFIA allergen entries ──────────────────────────

            var containsEntries = BuildAllergenEntries(containsMap);
            var mayContainEntries = BuildAllergenEntries(mayContainMap);

            // ── 6. Format bilingual declaration text ────────────────────────────

            var containsDecl = BuildDeclaration(
                containsEntries,
                englishPrefix: "Contains: ",
                frenchPrefix: "Contient\u00a0: ",
                suffix: ".");

            var mayContainDecl = BuildDeclaration(
                mayContainEntries,
                englishPrefix: "May contain: ",
                frenchPrefix: "Peut contenir\u00a0: ",
                suffix: ".");

            BilingualText? combinedLabel = null;
            if (containsDecl != null || mayContainDecl != null)
            {
                combinedLabel = new BilingualText
                {
                    English = string.Join(" ",
                        new[] { containsDecl?.English, mayContainDecl?.English }
                        .Where(s => s != null)),
                    French = string.Join(" ",
                        new[] { containsDecl?.French, mayContainDecl?.French }
                        .Where(s => s != null))
                };
            }

            return new CfiaAllergenDeclarationResult
            {
                ContainsDeclaration = containsDecl,
                MayContainDeclaration = mayContainDecl,
                CombinedLabel = combinedLabel,
                ContainsAllergens = containsEntries,
                MayContainAllergens = mayContainEntries,
                IngredientBreakdown = breakdown,
                UnmatchedIngredients = unmatchedNames,
                TotalIngredients = submittedNames.Count,
                MatchedIngredients = structuredMatchCount + keywordMatchCount,
                DetectionSummary = new DetectionSummary
                {
                    StructuredMatches = structuredMatchCount,
                    KeywordMatches = keywordMatchCount,
                    UnmatchedCount = unmatchedNames.Count
                }
            };
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static List<CfiaAllergenEntry> BuildAllergenEntries(
            Dictionary<string, HashSet<string>> map)
        {
            var entries = new List<CfiaAllergenEntry>();

            // Return in CFIA conventional order
            foreach (var (type, english, french) in CfiaAllergens)
            {
                if (!map.TryGetValue(type, out var sources)) continue;

                entries.Add(new CfiaAllergenEntry
                {
                    AllergenType = type,
                    EnglishName = english,
                    FrenchName = french,
                    SourceIngredients = sources.OrderBy(s => s).ToList()
                });
            }

            return entries;
        }

        private static BilingualText? BuildDeclaration(
            List<CfiaAllergenEntry> entries,
            string englishPrefix,
            string frenchPrefix,
            string suffix)
        {
            if (!entries.Any()) return null;

            return new BilingualText
            {
                English = englishPrefix + string.Join(", ", entries.Select(e => e.EnglishName)) + suffix,
                French  = frenchPrefix  + string.Join(", ", entries.Select(e => e.FrenchName))  + suffix
            };
        }
    }
}
