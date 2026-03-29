using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Allergy
{
    public interface IAllergenInteractionService
    {
        Task<AllergenInteractionResult> AnalyseInteractionsAsync(
            AllergenInteractionRequest request,
            CancellationToken ct = default);
    }
}
