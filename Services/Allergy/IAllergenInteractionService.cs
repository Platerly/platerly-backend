using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Allergy
{
    /// <summary>Analyses multi-allergen profiles for cross-reactive pairs, compound risk scoring, clinical recommendations, and combined safe food windows.</summary>
    public interface IAllergenInteractionService
    {
        Task<AllergenInteractionResult> AnalyseInteractionsAsync(
            AllergenInteractionRequest request,
            CancellationToken ct = default);
    }
}
