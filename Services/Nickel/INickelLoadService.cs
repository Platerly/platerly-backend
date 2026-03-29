using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Nickel
{
    public interface INickelLoadService
    {
        Task<NickelDailyLoadResponse> CalculateDailyLoadAsync(
            NickelDailyLoadRequest request,
            CancellationToken ct = default);
    }
}
