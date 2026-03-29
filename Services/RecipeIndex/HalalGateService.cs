using Platerly.Api.DTOs;

namespace Platerly.Api.Services.RecipeIndex
{
    /// <summary>
    /// Evaluates recipe ingredients against a comprehensive list of non-halal terms.
    ///
    /// Detection categories:
    ///   • Pork and all pork derivatives
    ///   • Alcohol and all alcohol-based ingredients
    ///   • Blood and blood products
    ///
    /// Ambiguity principle: when in doubt, discard.
    /// The cost of wrongly including a non-halal recipe is higher than discarding a borderline one.
    /// Exemption qualifiers must appear explicitly in the same ingredient string.
    /// </summary>
    public class HalalGateService : IHalalGateService
    {
        // ── Category constants ────────────────────────────────────────────────
        private const string Pork    = "Pork";
        private const string Alcohol = "Alcohol";
        private const string Blood   = "Blood";

        // ── Detection rule table ──────────────────────────────────────────────
        // Rules are evaluated in order. The first matching (and non-exempted) rule
        // for an ingredient produces a violation and stops further evaluation for
        // that ingredient. Each ingredient generates at most one HalalViolation.
        //
        // WordBoundary=true  → ContainsWord()  (e.g. "ham" won't match "hamburger")
        // WordBoundary=false → Contains()      (e.g. "lard" also catches "lardons")
        private static readonly HalalRule[] Rules =
        {
            // ── Pork — unconditional ──────────────────────────────────────────
            R("pork",        true,  Pork, "Contains pork"),
            R("pig",         true,  Pork, "Contains pig or pig-derived product"),
            R("swine",       true,  Pork, "Contains swine product"),
            R("bacon",       true,  Pork, "Contains bacon"),
            R("ham",         true,  Pork, "Contains ham (pork leg)"),
            R("prosciutto",  true,  Pork, "Contains prosciutto (Italian cured pork)"),
            R("pancetta",    true,  Pork, "Contains pancetta (Italian bacon)"),
            R("lard",        false, Pork, "Contains lard or lardons (rendered pork fat)"),
            R("pepperoni",   true,  Pork, "Contains pepperoni"),
            R("carnitas",    true,  Pork, "Contains carnitas (braised pork)"),
            R("mortadella",  true,  Pork, "Contains mortadella (pork sausage)"),
            R("guanciale",   true,  Pork, "Contains guanciale (cured pork cheek)"),
            R("capicola",    true,  Pork, "Contains capicola (cured pork)"),
            R("capicolla",   true,  Pork, "Contains capicolla (cured pork)"),
            R("speck",       true,  Pork, "Contains speck (smoked cured pork)"),
            R("nduja",       true,  Pork, "Contains 'nduja (spreadable pork sausage)"),
            R("lardo",       true,  Pork, "Contains lardo (cured pork fatback)"),
            R("coppa",       true,  Pork, "Contains coppa (cured pork neck)"),
            R("crackling",   true,  Pork, "Contains crackling (pork skin)"),
            R("scratchings", true,  Pork, "Contains pork scratchings"),
            R("chorizo",     true,  Pork, "Contains chorizo"),

            // ── Pork — with qualifying exemptions ────────────────────────────
            // gelatin: passes only when explicitly halal, bovine, or plant-based certified
            R("gelatin", true, Pork,
                "Contains unqualified gelatin (pork-derived unless explicitly certified halal, bovine, or plant-based)",
                "halal", "bovine", "plant-based", "agar", "vegetarian", "vegan", "kosher"),

            // sausage: WordBoundary=false to also catch "sausages" (plural).
            // Still exempt when "halal", "chicken", "beef", or "turkey" appears in the same string.
            R("sausage", false, Pork,
                "Contains unqualified sausage — may be pork. Must be explicitly labelled halal, chicken, beef, or turkey.",
                "halal", "chicken", "beef", "turkey"),

            // salami: passes only with an explicit halal label
            R("salami", true, Pork,
                "Contains unqualified salami — typically pork unless explicitly halal-labelled.",
                "halal"),

            // ribs: passes only when context explicitly names beef or lamb
            R("ribs", true, Pork,
                "Contains ribs without an explicit beef or lamb qualifier.",
                "beef", "lamb"),

            // ── Alcohol — unconditional ───────────────────────────────────────
            // Specific product names take priority in the list before generic "rum"/"brandy"
            R("rum extract",    false, Alcohol, "Contains rum extract (alcohol-based)"),
            R("brandy extract", false, Alcohol, "Contains brandy extract (alcohol-based)"),
            R("cooking wine",   false, Alcohol, "Contains cooking wine"),
            R("hard cider",     false, Alcohol, "Contains hard cider (alcoholic)"),
            R("beer batter",    false, Alcohol, "Contains beer batter"),
            R("wine sauce",     false, Alcohol, "Contains wine sauce"),
            R("wine reduction", false, Alcohol, "Contains wine reduction"),

            R("wine",      true, Alcohol, "Contains wine"),
            R("beer",      true, Alcohol, "Contains beer"),
            R("ale",       true, Alcohol, "Contains ale"),
            R("lager",     true, Alcohol, "Contains lager"),
            R("stout",     true, Alcohol, "Contains stout (dark beer)"),
            R("porter",    true, Alcohol, "Contains porter (dark beer)"),
            R("spirits",   true, Alcohol, "Contains spirits (distilled alcohol)"),
            R("whiskey",   true, Alcohol, "Contains whiskey"),
            R("whisky",    true, Alcohol, "Contains whisky"),
            R("vodka",     true, Alcohol, "Contains vodka"),
            R("rum",       true, Alcohol, "Contains rum"),
            R("brandy",    true, Alcohol, "Contains brandy"),
            R("cognac",    true, Alcohol, "Contains cognac"),
            R("champagne", true, Alcohol, "Contains champagne"),
            R("prosecco",  true, Alcohol, "Contains prosecco"),
            R("mirin",     true, Alcohol, "Contains mirin (Japanese rice wine)"),
            R("sake",      true, Alcohol, "Contains sake (Japanese rice wine)"),
            R("sherry",    true, Alcohol, "Contains sherry"),
            R("marsala",   true, Alcohol, "Contains marsala wine"),
            R("vermouth",  true, Alcohol, "Contains vermouth"),
            R("kahlua",    true, Alcohol, "Contains Kahlúa (coffee liqueur)"),
            R("baileys",   true, Alcohol, "Contains Baileys (cream liqueur)"),
            R("amaretto",  true, Alcohol, "Contains amaretto (almond liqueur)"),
            R("bourbon",   true, Alcohol, "Contains bourbon whiskey"),
            R("tequila",   true, Alcohol, "Contains tequila"),
            R("gin",       true, Alcohol, "Contains gin"),
            R("absinthe",  true, Alcohol, "Contains absinthe"),
            R("schnapps",  true, Alcohol, "Contains schnapps"),
            R("liqueur",   true, Alcohol, "Contains liqueur"),
            R("port",      true, Alcohol, "Contains port wine"),

            // cider: exempt when accompanied by "vinegar" (apple cider vinegar is halal)
            R("cider", true, Alcohol, "Contains cider (alcoholic unless labelled vinegar)", "vinegar"),

            // vanilla extract: requires explicit alcohol-free labelling to pass
            R("vanilla extract", false, Alcohol,
                "Contains vanilla extract (alcohol-based unless explicitly labelled alcohol-free or pure vanilla)",
                "alcohol-free", "alcohol free", "pure vanilla"),

            // ── Blood products ────────────────────────────────────────────────
            R("blood sausage", false, Blood, "Contains blood sausage"),
            R("black pudding", false, Blood, "Contains black pudding (blood sausage)"),
            R("blood pudding", false, Blood, "Contains blood pudding"),
            R("morcilla",      true,  Blood, "Contains morcilla (Spanish blood sausage)"),
            R("boudin noir",   false, Blood, "Contains boudin noir (French blood sausage)"),
            R("blutwurst",     true,  Blood, "Contains Blutwurst (German blood sausage)"),

            // "blood" standalone — exempt when "orange" is present (blood orange is halal)
            R("blood", true, Blood,
                "Contains blood product",
                "orange"),
        };

        // ── Public method ─────────────────────────────────────────────────────

        public HalalGateResult Evaluate(IReadOnlyList<string> ingredients)
        {
            var violations = new List<HalalViolation>();

            foreach (var ingredient in ingredients)
            {
                if (string.IsNullOrWhiteSpace(ingredient)) continue;

                var lower = ingredient.ToLowerInvariant();

                foreach (var rule in Rules)
                {
                    bool matched = rule.WordBoundary
                        ? ContainsWord(lower, rule.Term)
                        : lower.Contains(rule.Term, StringComparison.Ordinal);

                    if (!matched) continue;

                    // If any exemption keyword is present in the same ingredient string → skip
                    bool exempted = rule.Exemptions is { Length: > 0 } &&
                        rule.Exemptions.Any(e => lower.Contains(e, StringComparison.Ordinal));

                    if (!exempted)
                    {
                        violations.Add(new HalalViolation
                        {
                            IngredientName = ingredient,
                            MatchedTerm    = rule.Term,
                            Category       = rule.Category,
                            Reason         = rule.Reason
                        });
                        break; // One violation per ingredient — no need to check further rules
                    }
                }
            }

            return new HalalGateResult
            {
                Passed           = violations.Count == 0,
                FailedIngredients = violations
            };
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Checks whether <paramref name="text"/> contains <paramref name="word"/>
        /// as a whole word — i.e., not embedded inside a larger word.
        /// Treats hyphens and other punctuation as word boundaries, so
        /// "beer" matches "beer-battered" but "ham" does not match "hamburger".
        /// </summary>
        private static bool ContainsWord(string text, string word)
        {
            int start = 0;
            while (start <= text.Length - word.Length)
            {
                int idx = text.IndexOf(word, start, StringComparison.Ordinal);
                if (idx < 0) return false;

                bool leftOk  = idx == 0                         || !char.IsLetterOrDigit(text[idx - 1]);
                bool rightOk = (idx + word.Length) >= text.Length || !char.IsLetterOrDigit(text[idx + word.Length]);

                if (leftOk && rightOk) return true;
                start = idx + 1;
            }
            return false;
        }

        /// <summary>
        /// Factory shorthand for building a <see cref="HalalRule"/>.
        /// Term and exemptions are lowercased at construction time.
        /// </summary>
        private static HalalRule R(
            string term,
            bool   wordBoundary,
            string category,
            string reason,
            params string[] exemptions)
            => new(
                term.ToLowerInvariant(),
                wordBoundary,
                category,
                reason,
                exemptions.Length > 0 ? exemptions.Select(e => e.ToLowerInvariant()).ToArray() : null);

        // ── Internal rule record ──────────────────────────────────────────────

        private readonly record struct HalalRule(
            string    Term,
            bool      WordBoundary,
            string    Category,
            string    Reason,
            string[]? Exemptions);
    }
}
