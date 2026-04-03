using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    /// <summary>Provides B2B Phase 2 recipe safety auditing for allergen and prohibited-ingredient detection, with optional allergen-profile scoping.</summary>
    public interface IRecipeSafetyService
    {
        Task<RecipeSafetyAuditResponse> AuditRecipeAsync(
            RecipeSafetyAuditRequest request,
            CancellationToken ct = default);

        Task<BatchAuditResponse> BatchAuditAsync(
            BatchAuditRequest request,
            CancellationToken ct = default);
    }
}
