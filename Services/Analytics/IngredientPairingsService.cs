using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    /// <summary>
    /// B2B data layer — ingredient pairing analytics sourced from co-occurrence data in the recipe index.
    /// Depends on <see cref="PlaterlyDbContext"/>.
    ///
    /// Resolves a seed ingredient by ID or name, then queries the <c>IngredientPairings</c> table
    /// for the most common co-occurring ingredients. Nickel classification is included per pairing
    /// to support dietary safety filtering by B2B consumers.
    /// </summary>
    public class IngredientPairingsService : IIngredientPairingsService
    {
        private readonly PlaterlyDbContext _db;

        public IngredientPairingsService(PlaterlyDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Returns the top co-occurring ingredient pairings for a seed ingredient.
        /// The seed can be specified by <c>IngredientId</c> or by name; returns an empty response
        /// (not an error) when the seed ingredient is not found.
        /// </summary>
        /// <param name="request">Seed ingredient ID or name, and take count.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>An <see cref="IngredientPairingsResponse"/> with the seed and its top pairings.</returns>
        public async Task<IngredientPairingsResponse> GetPairingsAsync(
            IngredientPairingsRequest request,
            CancellationToken ct = default)
        {
            // ── Resolve seed ingredient ─────────────────────────────────────────

            IngredientSummaryDto? seed = null;

            if (request.IngredientId.HasValue)
            {
                var seedEntity = await _db.GlobalIngredients
                    .AsNoTracking()
                    .Where(i => i.Id == request.IngredientId.Value)
                    .Select(i => new
                    {
                        i.Id,
                        i.Name,
                        CategoryName = i.Category != null ? i.Category.Name : null,
                        NickelClass = i.NickelClassification.ToString()
                    })
                    .FirstOrDefaultAsync(ct);

                if (seedEntity == null)
                    return new IngredientPairingsResponse(); // service returns empty; controller handles 404

                seed = new IngredientSummaryDto
                {
                    Id = seedEntity.Id,
                    Name = seedEntity.Name,
                    Category = seedEntity.CategoryName,
                    NickelClassification = seedEntity.NickelClass
                };
            }

            // ── Query pairings ──────────────────────────────────────────────────

            var query = _db.IngredientPairings
                .AsNoTracking()
                .Where(p =>
                    p.CoOccurrenceCount >= request.MinCoOccurrenceCount &&
                    p.AffinityScore >= request.MinAffinityScore);

            if (request.IngredientId.HasValue)
            {
                var id = request.IngredientId.Value;
                query = query.Where(p => p.IngredientAId == id || p.IngredientBId == id);
            }

            if (!string.IsNullOrWhiteSpace(request.Region))
                query = query.Where(p => p.TopRegion == request.Region);

            if (!string.IsNullOrWhiteSpace(request.Cuisine))
                query = query.Where(p =>
                    p.CommonCuisines != null && p.CommonCuisines.Contains(request.Cuisine));

            // Sort
            var sorted = request.SortBy.ToLower() == "cooccurrence"
                ? query.OrderByDescending(p => p.CoOccurrenceCount)
                : query.OrderByDescending(p => p.AffinityScore);

            var rawPairings = await sorted
                .Take(request.Take)
                .Select(p => new
                {
                    p.IngredientAId,
                    p.IngredientBId,
                    p.CoOccurrenceCount,
                    p.AffinityScore,
                    p.TopRegion,
                    p.CommonCuisines,
                    p.LastUpdated
                })
                .ToListAsync(ct);

            if (!rawPairings.Any())
                return new IngredientPairingsResponse { SeedIngredient = seed };

            // ── Resolve partner ingredient metadata ─────────────────────────────

            var partnerIds = rawPairings.Select(p =>
                    request.IngredientId.HasValue
                        ? (p.IngredientAId == request.IngredientId.Value ? p.IngredientBId : p.IngredientAId)
                        // When no seed, collect both
                        : p.IngredientBId)
                .Distinct()
                .ToHashSet();

            // Also include IngredientA IDs when no seed filter
            if (!request.IngredientId.HasValue)
            {
                foreach (var p in rawPairings) partnerIds.Add(p.IngredientAId);
            }

            var partnerIngredients = await _db.GlobalIngredients
                .AsNoTracking()
                .Where(i => partnerIds.Contains(i.Id))
                .Select(i => new
                {
                    i.Id,
                    i.Name,
                    CategoryName = i.Category != null ? i.Category.Name : null,
                    NickelClass = i.NickelClassification.ToString()
                })
                .ToListAsync(ct);

            var partnerMap = partnerIngredients.ToDictionary(i => i.Id);

            // ── Build response DTOs ─────────────────────────────────────────────

            var pairings = rawPairings
                .Select(p =>
                {
                    var partnerId = request.IngredientId.HasValue
                        ? (p.IngredientAId == request.IngredientId.Value ? p.IngredientBId : p.IngredientAId)
                        : p.IngredientBId;

                    if (!partnerMap.TryGetValue(partnerId, out var partner))
                        return null;

                    return new B2BIngredientPairingDto
                    {
                        PairedIngredient = new IngredientSummaryDto
                        {
                            Id = partner.Id,
                            Name = partner.Name,
                            Category = partner.CategoryName,
                            NickelClassification = partner.NickelClass
                        },
                        CoOccurrenceCount = p.CoOccurrenceCount,
                        AffinityScore = p.AffinityScore,
                        TopRegion = p.TopRegion,
                        CommonCuisines = p.CommonCuisines,
                        LastUpdated = p.LastUpdated
                    };
                })
                .Where(p => p != null)
                .Select(p => p!)
                .ToList();

            return new IngredientPairingsResponse
            {
                SeedIngredient = seed,
                Pairings = pairings
            };
        }
    }
}
