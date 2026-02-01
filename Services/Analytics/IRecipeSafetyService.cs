using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
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
