using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Compliance
{
    public interface ICfiaAllergenDeclarationService
    {
        Task<CfiaAllergenDeclarationResult> GenerateDeclarationAsync(
            CfiaAllergenDeclarationRequest request,
            CancellationToken ct = default);
    }
}
