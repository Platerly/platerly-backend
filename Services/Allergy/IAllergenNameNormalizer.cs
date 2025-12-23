namespace Platerly.Api.Services.Allergy
{
    public interface IAllergenNameNormalizer
    {
        /// Returns canonical allergy names (e.g., "Nickel", "Gluten", "Sulfites") for a set of tokens.
        Task<IReadOnlyCollection<string>> MapToAllergyNamesAsync(IEnumerable<string> tokens, CancellationToken ct = default);

        /// Convenience for a single token.
        Task<IReadOnlyCollection<string>> MapToAllergyNamesAsync(string token, CancellationToken ct = default);
    }
}
