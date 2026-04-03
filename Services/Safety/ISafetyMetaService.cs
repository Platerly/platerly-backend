using Platerly.Api.DTOs;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Safety
{
    /// <summary>Builds <c>SafetyMeta</c> blocks (confidence, freshness, disclaimer, alerts) for substitution and allergen-alert responses.</summary>
    public interface ISafetyMetaService
    {
        /// <summary>
        /// Build the full SafetyMeta block for a substitution response.
        /// Evaluates confidence, freshness, and out-of-range alerts for every substitute.
        /// </summary>
        SafetyMeta BuildForSubstitutions(IEnumerable<SubstitutionDto> substitutions);

        /// <summary>
        /// Build SafetyMeta for an allergen alert response.
        /// </summary>
        SafetyMeta BuildForAllergyAlerts(RecipeAlertsResponse alerts);

        /// <summary>
        /// Build a baseline SafetyMeta with disclaimer + freshness only (no domain-specific checks).
        /// Used for endpoints that return data without specific nickel/allergen context.
        /// </summary>
        SafetyMeta BuildBaseline();
    }
}
