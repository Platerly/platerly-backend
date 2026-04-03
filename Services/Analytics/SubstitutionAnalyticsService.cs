using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Analytics
{
    /// <summary>
    /// B2B data layer — analytics over the ingredient substitution database for food-tech partners.
    /// Depends on <see cref="PlaterlyDbContext"/>.
    ///
    /// Provides top substitutions (optionally filtered by reason or allergy), per-ingredient
    /// substitution lookups, and a breakdown of substitution reasons across the database.
    /// </summary>
    public class SubstitutionAnalyticsService : ISubstitutionAnalyticsService
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<SubstitutionAnalyticsService> _logger;

        public SubstitutionAnalyticsService(
            PlaterlyDbContext db,
            ILogger<SubstitutionAnalyticsService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Returns the top active ingredient substitutions, optionally filtered by reason
        /// (e.g. AllergyFree, DietaryPreference) and allergy ID, ordered by confidence score.
        /// </summary>
        /// <param name="request">Optional filters and take count.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A <see cref="TopSubstitutionsResponse"/> with scored substitution pairs.</returns>
        public async Task<TopSubstitutionsResponse> GetTopSubstitutionsAsync(
            TopSubstitutionsRequest request,
            CancellationToken ct = default)
        {
            var query = _db.IngredientSubstitutions
                .Include(s => s.OriginalIngredient)
                .Include(s => s.SubstituteIngredient)
                .Include(s => s.Allergy)
                .Where(s => s.IsActive);

            if (!string.IsNullOrEmpty(request.Reason) &&
                Enum.TryParse<SubstitutionReason>(request.Reason, true, out var reason))
            {
                query = query.Where(s => s.Reason == reason);
            }

            if (request.AllergyId.HasValue)
            {
                query = query.Where(s => s.AllergyId == request.AllergyId.Value);
            }

            var totalCount = await query.CountAsync(ct);

            var substitutions = await query
                .OrderByDescending(s => s.UsageCount)
                .ThenByDescending(s => s.ConfidenceScore)
                .Take(request.Take)
                .Select(s => new TopSubstitutionDto
                {
                    SubstitutionId = s.Id,
                    OriginalIngredientId = s.OriginalIngredientId,
                    OriginalIngredientName = s.OriginalIngredient.Name,
                    SubstituteIngredientId = s.SubstituteIngredientId,
                    SubstituteIngredientName = s.SubstituteIngredient.Name,
                    Reason = s.Reason.ToString(),
                    AllergyName = s.Allergy != null ? s.Allergy.Name : null,
                    ConversionRatio = s.ConversionRatio,
                    ConfidenceScore = s.ConfidenceScore,
                    PositiveVotes = s.PositiveVotes,
                    NegativeVotes = s.NegativeVotes,
                    UsageCount = s.UsageCount,
                    IsVerified = s.IsVerified,
                    Notes = s.Notes
                })
                .ToListAsync(ct);

            return new TopSubstitutionsResponse
            {
                Substitutions = substitutions,
                TotalSubstitutions = totalCount,
                ReasonFilter = request.Reason
            };
        }

        public async Task<IngredientSubstitutionsResponse> GetSubstitutionsForIngredientAsync(
            IngredientSubstitutionsRequest request,
            CancellationToken ct = default)
        {
            var ingredient = await _db.GlobalIngredients
                .Where(g => g.Id == request.IngredientId)
                .Select(g => new { g.Id, g.Name })
                .FirstOrDefaultAsync(ct);

            if (ingredient == null)
            {
                return new IngredientSubstitutionsResponse
                {
                    IngredientId = request.IngredientId,
                    IngredientName = "Unknown",
                    Substitutions = new(),
                    TotalSubstitutions = 0
                };
            }

            var query = _db.IngredientSubstitutions
                .Include(s => s.OriginalIngredient)
                .Include(s => s.SubstituteIngredient)
                .Include(s => s.Allergy)
                .Where(s => s.IsActive &&
                    (s.OriginalIngredientId == request.IngredientId ||
                     s.SubstituteIngredientId == request.IngredientId));

            if (!string.IsNullOrEmpty(request.Reason) &&
                Enum.TryParse<SubstitutionReason>(request.Reason, true, out var reason))
            {
                query = query.Where(s => s.Reason == reason);
            }

            var totalCount = await query.CountAsync(ct);

            var substitutions = await query
                .OrderByDescending(s => s.ConfidenceScore)
                .ThenByDescending(s => s.UsageCount)
                .Take(request.Take)
                .Select(s => new TopSubstitutionDto
                {
                    SubstitutionId = s.Id,
                    OriginalIngredientId = s.OriginalIngredientId,
                    OriginalIngredientName = s.OriginalIngredient.Name,
                    SubstituteIngredientId = s.SubstituteIngredientId,
                    SubstituteIngredientName = s.SubstituteIngredient.Name,
                    Reason = s.Reason.ToString(),
                    AllergyName = s.Allergy != null ? s.Allergy.Name : null,
                    ConversionRatio = s.ConversionRatio,
                    ConfidenceScore = s.ConfidenceScore,
                    PositiveVotes = s.PositiveVotes,
                    NegativeVotes = s.NegativeVotes,
                    UsageCount = s.UsageCount,
                    IsVerified = s.IsVerified,
                    Notes = s.Notes
                })
                .ToListAsync(ct);

            return new IngredientSubstitutionsResponse
            {
                IngredientId = ingredient.Id,
                IngredientName = ingredient.Name,
                Substitutions = substitutions,
                TotalSubstitutions = totalCount
            };
        }

        public async Task<SubstitutionReasonBreakdownResponse> GetReasonBreakdownAsync(
            CancellationToken ct = default)
        {
            var totalCount = await _db.IngredientSubstitutions
                .Where(s => s.IsActive)
                .CountAsync(ct);

            var reasons = await _db.IngredientSubstitutions
                .Where(s => s.IsActive)
                .GroupBy(s => s.Reason)
                .Select(g => new SubstitutionReasonDto
                {
                    Reason = g.Key.ToString(),
                    Count = g.Count(),
                    Percentage = totalCount > 0
                        ? Math.Round((decimal)g.Count() / totalCount * 100, 2)
                        : 0,
                    VerifiedCount = g.Count(s => s.IsVerified),
                    AverageConfidenceScore = g.Average(s => (double)s.ConfidenceScore)
                })
                .OrderByDescending(r => r.Count)
                .ToListAsync(ct);

            return new SubstitutionReasonBreakdownResponse
            {
                Reasons = reasons,
                TotalSubstitutions = totalCount
            };
        }
    }
}
