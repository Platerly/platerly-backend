using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Compliance
{
    /// <summary>Generates bilingual CFIA-compliant allergen declarations for Canadian food packaging per SFCR s.257.</summary>
    public interface ICfiaAllergenDeclarationService
    {
        Task<CfiaAllergenDeclarationResult> GenerateDeclarationAsync(
            CfiaAllergenDeclarationRequest request,
            CancellationToken ct = default);
    }
}
