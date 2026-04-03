namespace Platerly.Api.Services.Allergy
{
    /// <summary>Maps raw allergen tokens to canonical CFIA/FDA allergen names (e.g., "dairy" → "Milk", "gluten" → "Gluten").</summary>
    public interface IAllergenNameNormalizer
    {
        /// Returns canonical allergy names (e.g., "Nickel", "Gluten", "Sulfites") for a set of tokens.
        Task<IReadOnlyCollection<string>> MapToAllergyNamesAsync(IEnumerable<string> tokens, CancellationToken ct = default);

        /// Convenience for a single token.
        Task<IReadOnlyCollection<string>> MapToAllergyNamesAsync(string token, CancellationToken ct = default);
    }
}
