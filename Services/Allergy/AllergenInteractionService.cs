using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Allergy
{
    /// <summary>
    /// Analyses a multi-allergen patient profile for cross-reactive pairs,
    /// calculates a compound systemic risk score, and queries the combined safe food window.
    ///
    /// Cross-reactivity knowledge base is embedded in code — these are established
    /// immunological relationships that change rarely and do not need DB storage.
    ///
    /// Compound risk score formula:
    ///   baseScore = min(allergenCount × 8, 40)
    ///   + crossReactivityBonus (HIGH pair = +12, MODERATE pair = +6, LOW pair = +3)
    ///   + anaphylaxisBonus    (peanuts/tree nuts/shellfish/fish/sesame = +5 each, max +20)
    ///   clamped 0–100
    ///
    /// Risk classification bands:
    ///   STANDARD 0–25 | ELEVATED 26–50 | HIGH 51–75 | SEVERE 76–100
    /// </summary>
    public class AllergenInteractionService : IAllergenInteractionService
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<AllergenInteractionService> _logger;

        // ── Cross-reactivity knowledge base ────────────────────────────────────
        // Sources: Sicherer SH 2001, Portnoy 2020, EAACI allergen cross-reactivity maps,
        // Beyer K et al. 2001 (peanut-tree nut), PRACTALL consensus.
        // AllergenA, AllergenB, Mechanism, CoSensitisationLikelihoodPercent, ClinicalSignificance,
        // ClinicalNote, RiskContribution

        private static readonly CrossReactivityEntry[] KnowledgeBase =
        {
            new(
                "Peanuts", "Soy",
                "Legume family — shared vicilin and legumin seed storage proteins",
                10,
                "LOW",
                "Peanut and soy are both legumes but clinical cross-reactivity is low (~5–10%). " +
                      "Most peanut-allergic patients tolerate soy. Soy avoidance is not routinely recommended " +
                      "unless confirmed by oral food challenge.",
                3),

            new(
                "Peanuts", "Tree Nuts",
                "High co-sensitisation in allergic population — not classical cross-reactivity",
                40,
                "MODERATE",
                "Peanut and tree nut allergies frequently co-occur (~40% of peanut-allergic patients). " +
                      "This is largely due to co-sensitisation rather than protein cross-reactivity. " +
                      "Tree nut avoidance is typically advised as a precautionary measure. " +
                      "Consider allergen-specific IgE testing before broad tree nut avoidance.",
                6),

            new(
                "Peanuts", "Sesame",
                "2S albumin (Sin a 1 / Ara h 2 / Ara h 6) seed storage protein family homology",
                25,
                "MODERATE",
                "Sesame and peanut share homologous 2S albumin proteins. Co-sensitisation is reported " +
                      "in ~25% of peanut-allergic patients. Both carry high anaphylaxis potential. " +
                      "Sesame labelling is now mandatory in Canada, US, and EU — advise label vigilance.",
                6),

            new(
                "Tree Nuts", "Sesame",
                "2S albumin protein family shared between sesame (Ses i 1) and tree nuts (cashew Ana o 3)",
                20,
                "MODERATE",
                "Sesame shares 2S albumin epitopes with several tree nuts (particularly cashew, pistachio). " +
                      "Consider sesame screening when managing tree nut allergy, especially if reactions are severe.",
                6),

            new(
                "Mustard", "Sesame",
                "2S albumin / nsLTP protein family cross-reactivity (Brassica–Sesamum homology)",
                22,
                "MODERATE",
                "Mustard (Sin a 1, a 2S albumin) shares structural similarity with sesame allergens. " +
                      "Co-sensitisation is observed particularly in patients with multiple seed allergies. " +
                      "Both are CFIA and EU priority allergens — mandatory declaration applies.",
                6),

            new(
                "Mustard", "Peanuts",
                "2S albumin protein family (Sin a 1 vs Ara h 2/Ara h 6)",
                15,
                "LOW",
                "Structural homology between mustard 2S albumins and peanut Ara h 2/Ara h 6 can cause " +
                      "co-sensitisation in seed-allergic patients, though clinical cross-reactivity is uncommon.",
                3),

            new(
                "Fish", "Shellfish",
                "Different protein classes (parvalbumin vs tropomyosin) — high clinical co-occurrence",
                38,
                "MODERATE",
                "Fish (parvalbumin) and shellfish (tropomyosin) allergies are caused by different proteins " +
                      "with no direct cross-reactivity. However, ~38% of fish-allergic patients are also " +
                      "shellfish-allergic due to shared dietary patterns and atopic background. " +
                      "Both are high anaphylaxis risk — co-management is strongly recommended.",
                6),

            new(
                "Shellfish", "Gluten",
                "No molecular cross-reactivity — co-occurrence in adults with multiple food allergies",
                15,
                "LOW",
                "Shellfish and gluten allergies frequently co-occur in adult-onset food allergy presentations. " +
                      "No shared protein mechanism. Independent management required.",
                3),

            new(
                "Milk", "Eggs",
                "No molecular cross-reactivity — very high co-sensitisation in paediatric allergy",
                50,
                "HIGH",
                "Milk and egg allergies are the two most common paediatric food allergies and co-occur " +
                      "in ~50% of affected children. Both frequently resolve with age (tolerance induction). " +
                      "Combined avoidance significantly restricts diet — dietitian referral is strongly recommended " +
                      "to ensure adequate calcium, protein, and micronutrient intake.",
                12),

            new(
                "Milk", "Soy",
                "No molecular cross-reactivity — high co-occurrence in cow's milk protein intolerance",
                30,
                "MODERATE",
                "Approximately 20–30% of cow's milk allergic infants also react to soy formula. " +
                      "This is thought to reflect general gastrointestinal sensitisation rather than protein homology. " +
                      "Extensively hydrolysed or amino-acid-based formula may be required in affected infants.",
                6),

            new(
                "Gluten", "Soy",
                "No molecular cross-reactivity — common co-restriction in elimination diets",
                12,
                "LOW",
                "Gluten and soy avoidance frequently co-occur in elimination diets (particularly for EoE " +
                      "and non-IgE-mediated GI conditions). Combined avoidance restricts many processed foods — " +
                      "whole food dietary planning with a dietitian is recommended.",
                3),

            new(
                "Milk", "Gluten",
                "No molecular cross-reactivity — high dietary overlap restriction",
                18,
                "LOW",
                "Milk and gluten avoidance combined eliminates a large proportion of processed foods and " +
                      "bakery products. Calcium supplementation is particularly important when dairy is excluded " +
                      "alongside gluten-containing whole grains.",
                3),
        };

        // Allergens with documented high anaphylaxis risk — add to risk score
        private static readonly HashSet<string> AnaphylaxisProneAllergens = new(StringComparer.OrdinalIgnoreCase)
        {
            "Peanuts", "Tree Nuts", "Shellfish", "Fish", "Sesame"
        };

        // All allergen types present in the knowledge base
        private static readonly HashSet<string> KnownAllergens = new(
            KnowledgeBase.SelectMany(e => new[] { e.AllergenA, e.AllergenB }),
            StringComparer.OrdinalIgnoreCase)
        {
            // Include all 11 CFIA priority allergens even if not in a pair
            "Milk", "Eggs", "Soy", "Gluten", "Mustard", "Sulfites"
        };

        public AllergenInteractionService(
            PlaterlyDbContext db,
            ILogger<AllergenInteractionService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Analyses a multi-allergen profile for cross-reactive pairs, computes the compound risk
        /// score, derives clinical recommendations, and optionally queries the combined safe food
        /// window from <c>GlobalIngredients</c>.
        ///
        /// Processing order:
        /// <list type="number">
        ///   <item>Submitted allergens are de-duplicated and split into recognised vs. unknown.</item>
        ///   <item>Cross-reactive pairs are detected by scanning the embedded knowledge base.</item>
        ///   <item>Compound risk score is computed: baseScore + crossBonus + anaphylaxisBonus, clamped 0–100.</item>
        ///   <item>Risk class is assigned: STANDARD ≤25 | ELEVATED ≤50 | HIGH ≤75 | SEVERE ≤100.</item>
        ///   <item>Clinical recommendations are built per risk class and per HIGH/MODERATE pair.</item>
        ///   <item>When <c>IncludeSafeFoods</c> is true, queries verified active ingredients whose
        ///     <c>AllergenType</c> list contains none of the recognised allergens, ordered by usage.</item>
        /// </list>
        /// </summary>
        /// <param name="request">Submitted allergen list, safe-food flag, and limit.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Full interaction result including pairs, score, recommendations, and optional safe foods.</returns>
        public async Task<AllergenInteractionResult> AnalyseInteractionsAsync(
            AllergenInteractionRequest request,
            CancellationToken ct = default)
        {
            var submitted = request.Allergens
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var recognised = submitted.Where(a => KnownAllergens.Contains(a)).ToList();
            var unknown    = submitted.Except(recognised, StringComparer.OrdinalIgnoreCase).ToList();

            // ── Detect cross-reactive pairs ──────────────────────────────────────

            var detectedPairs = new List<CrossReactivePair>();
            var allergensInPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in KnowledgeBase)
            {
                bool hasA = recognised.Contains(entry.AllergenA, StringComparer.OrdinalIgnoreCase);
                bool hasB = recognised.Contains(entry.AllergenB, StringComparer.OrdinalIgnoreCase);

                if (hasA && hasB)
                {
                    detectedPairs.Add(new CrossReactivePair
                    {
                        AllergenA = entry.AllergenA,
                        AllergenB = entry.AllergenB,
                        Mechanism = entry.Mechanism,
                        CoSensitisationLikelihoodPercent = entry.CoSensitisationLikelihood,
                        ClinicalSignificance = entry.Significance,
                        ClinicalNote = entry.Note,
                        RiskContribution = entry.RiskContribution
                    });
                    allergensInPairs.Add(entry.AllergenA);
                    allergensInPairs.Add(entry.AllergenB);
                }
            }

            var isolatedAllergens = recognised
                .Where(a => !allergensInPairs.Contains(a))
                .ToList();

            // ── Compound risk score ──────────────────────────────────────────────

            // Base: allergen count component
            int baseScore = Math.Min(recognised.Count * 8, 40);

            // Cross-reactivity bonus per detected pair
            int crossBonus = detectedPairs.Sum(p => p.RiskContribution);

            // Anaphylaxis-prone allergen bonus (capped at 20)
            int anaphylaxisBonus = Math.Min(
                recognised.Count(a => AnaphylaxisProneAllergens.Contains(a)) * 5,
                20);

            int rawScore = baseScore + crossBonus + anaphylaxisBonus;
            int compoundScore = Math.Clamp(rawScore, 0, 100);

            string riskClass = compoundScore switch
            {
                <= 25 => "STANDARD",
                <= 50 => "ELEVATED",
                <= 75 => "HIGH",
                _     => "SEVERE"
            };

            string explanation =
                $"Base score: {baseScore} ({recognised.Count} allergens × 8, capped at 40). " +
                $"Cross-reactivity bonus: {crossBonus} ({detectedPairs.Count} pair(s) detected). " +
                $"Anaphylaxis-prone bonus: {anaphylaxisBonus} " +
                $"({recognised.Count(a => AnaphylaxisProneAllergens.Contains(a))} high-risk allergen(s) × 5, capped at 20). " +
                $"Total: {compoundScore}/100 → {riskClass}.";

            // ── Clinical recommendations ─────────────────────────────────────────

            var recommendations = BuildRecommendations(riskClass, detectedPairs, recognised, isolatedAllergens);

            // ── Effective allergen filter (submitted + cross-reactive implications) ─

            // For the safe food window, we filter on submitted allergens only
            // (cross-reactivity is probabilistic, not definitive)
            var effectiveFilter = recognised.ToList();

            // ── Combined safe food window ────────────────────────────────────────

            List<SafeFoodEntry>? safeFoods = null;

            if (request.IncludeSafeFoods && recognised.Any())
            {
                int limit = Math.Clamp(request.SafeFoodLimit, 1, 100);

                var safeGlobalIngredients = await _db.GlobalIngredients
                    .AsNoTracking()
                    .Include(gi => gi.Allergens)
                    .Include(gi => gi.Category)
                    .Where(gi => gi.IsActive &&
                                 gi.IsVerified &&
                                 gi.Allergens.Any() &&
                                 !gi.Allergens.Any(a =>
                                     effectiveFilter.Contains(a.AllergenType)))
                    .OrderByDescending(gi => gi.TotalUsageCount)
                    .Take(limit)
                    .ToListAsync(ct);

                safeFoods = safeGlobalIngredients.Select(gi => new SafeFoodEntry
                {
                    Name                = gi.Name,
                    Category            = gi.Category?.Name,
                    NutritionalCategory = gi.NutritionalCategory,
                    IsVerified          = gi.IsVerified
                }).ToList();
            }

            return new AllergenInteractionResult
            {
                RecognisedAllergens    = recognised,
                UnknownAllergens       = unknown,
                CrossReactivePairs     = detectedPairs,
                IsolatedAllergens      = isolatedAllergens,
                CompoundRiskScore      = compoundScore,
                RiskClassification     = riskClass,
                RiskScoreExplanation   = explanation,
                ClinicalRecommendations = recommendations,
                CombinedSafeFoods      = safeFoods,
                EffectiveAllergenFilter = effectiveFilter
            };
        }

        // ── Recommendation builder ─────────────────────────────────────────────

        private static List<string> BuildRecommendations(
            string riskClass,
            List<CrossReactivePair> pairs,
            List<string> recognised,
            List<string> isolated)
        {
            var recs = new List<string>();

            // Risk class–level recommendations
            switch (riskClass)
            {
                case "SEVERE":
                    recs.Add("SEVERE RISK: Refer to a specialist allergist for comprehensive allergen panel testing " +
                             "and individualised management plan. Patient should carry two doses of epinephrine auto-injector " +
                             "(e.g., EpiPen) at all times.");
                    recs.Add("Referral to a registered dietitian with food allergy expertise is strongly recommended " +
                             "to prevent nutritional deficiencies across multiple food group restrictions.");
                    break;
                case "HIGH":
                    recs.Add("HIGH RISK: Dietitian referral recommended. Multiple simultaneous food restrictions " +
                             "create significant risk of macronutrient and micronutrient deficiency. " +
                             "Review calcium, protein, iron, and B12 status.");
                    recs.Add("Consider epinephrine auto-injector prescription given co-presence of anaphylaxis-prone allergens.");
                    break;
                case "ELEVATED":
                    recs.Add("ELEVATED RISK: Reinforce label reading across all allergen categories. " +
                             "Cross-contamination risk is compounded when multiple allergens are present.");
                    break;
                default:
                    recs.Add("STANDARD RISK: Follow standard multi-allergen avoidance protocol. " +
                             "Provide written allergen list for patient and caregivers.");
                    break;
            }

            // Per-pair recommendations from the knowledge base
            foreach (var pair in pairs.Where(p => p.ClinicalSignificance is "HIGH" or "MODERATE"))
            {
                recs.Add($"{pair.AllergenA} + {pair.AllergenB}: {pair.ClinicalNote}");
            }

            // General for isolated high-risk allergens
            foreach (var a in isolated.Where(a => AnaphylaxisProneAllergens.Contains(a)))
            {
                recs.Add($"{a}: Anaphylaxis-prone allergen. Ensure epinephrine auto-injector access and " +
                         "patient education on early symptom recognition.");
            }

            // Sulfites note if present
            if (recognised.Any(a => string.Equals(a, "Sulfites", StringComparison.OrdinalIgnoreCase)))
            {
                recs.Add("Sulfites: Intolerance mechanism is non-IgE-mediated (unlike other CFIA priority allergens). " +
                         "Reaction is dose-dependent — advise avoidance of high-sulfite foods: " +
                         "wine, beer, dried fruit, pickled vegetables, and commercial potato products.");
            }

            return recs;
        }

        // ── Internal knowledge base record ─────────────────────────────────────

        private record CrossReactivityEntry(
            string AllergenA,
            string AllergenB,
            string Mechanism,
            int CoSensitisationLikelihood,
            string Significance,
            string Note,
            int RiskContribution);
    }
}
