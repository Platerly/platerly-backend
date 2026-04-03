using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Nickel
{
    /// <summary>Calculates cumulative daily nickel intake from a meal plan against the 150 μg/day clinical threshold (Mislankar &amp; Zirwas 2013).</summary>
    public interface INickelLoadService
    {
        Task<NickelDailyLoadResponse> CalculateDailyLoadAsync(
            NickelDailyLoadRequest request,
            CancellationToken ct = default);
    }
}
