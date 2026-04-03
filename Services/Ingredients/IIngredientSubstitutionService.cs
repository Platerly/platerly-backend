using Platerly.Api.DTOs;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Ingredients
{
    /// <summary>Provides allergen-safe ingredient substitution lookups, bulk queries, voting, and crowdsourced suggestions.</summary>
    public interface IIngredientSubstitutionService
    {
        /// <summary>
        /// Get all substitutions for a specific ingredient
        /// </summary>
        Task<List<SubstitutionDto>> GetSubstitutionsForIngredientAsync(
            Guid ingredientId,
            CancellationToken ct = default);

        /// <summary>
        /// Get substitutions filtered by allergy ID
        /// </summary>
        Task<List<SubstitutionDto>> GetSubstitutionsByAllergyAsync(
            Guid allergyId,
            CancellationToken ct = default);

        /// <summary>
        /// Get substitutions filtered by allergy name
        /// </summary>
        Task<List<SubstitutionDto>> GetSubstitutionsByAllergyNameAsync(
            string allergyName,
            CancellationToken ct = default);

        /// <summary>
        /// Get substitutions by reason (Allergy, DietaryPreference, Religious, etc.)
        /// </summary>
        Task<List<SubstitutionDto>> GetSubstitutionsByReasonAsync(
            string reason,
            CancellationToken ct = default);

        /// <summary>
        /// Get substitutions for an ingredient name (fuzzy match)
        /// </summary>
        Task<List<SubstitutionDto>> GetSubstitutionsForIngredientNameAsync(
            string ingredientName,
            List<Guid>? userAllergyIds = null,
            CancellationToken ct = default);

        /// <summary>
        /// Suggest a new substitution (crowdsourced)
        /// </summary>
        Task<SubstitutionDto> SuggestSubstitutionAsync(
            SuggestSubstitutionDto dto,
            Guid userId,
            CancellationToken ct = default);

        /// <summary>
        /// Vote on a substitution
        /// </summary>
        Task<SubstitutionDto> VoteOnSubstitutionAsync(
            Guid substitutionId,
            Guid userId,
            bool isPositive,
            string? comment = null,
            CancellationToken ct = default);

        /// <summary>
        /// Get bulk substitutions for multiple ingredients based on user allergies
        /// </summary>
        Task<List<BulkSubstitutionResponseDto>> GetBulkSubstitutionsAsync(
            BulkSubstitutionRequestDto request,
            CancellationToken ct = default);

        /// <summary>
        /// Record that a substitution was used
        /// </summary>
        Task IncrementUsageCountAsync(Guid substitutionId, CancellationToken ct = default);

        /// <summary>
        /// Get top substitutions (most used, highest rated)
        /// </summary>
        Task<List<SubstitutionDto>> GetTopSubstitutionsAsync(
            int take = 20,
            CancellationToken ct = default);
    }
}
