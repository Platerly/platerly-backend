using Platerly.Api.DTOs;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Safety
{
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
