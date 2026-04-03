using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Analytics
{
    /// <summary>
    /// B2B data layer — provides aggregate allergen statistics for the B2B analytics API.
    /// Depends on <see cref="PlaterlyDbContext"/>.
    ///
    /// Exposes three queries:
    ///   <see cref="GetPrevalenceAsync"/> — allergen prevalence by user count and percentage across the platform.
    ///   <see cref="GetIngredientsByAllergenAsync"/> — paginated list of ingredients that contain a given allergen.
    ///   <see cref="GetSafeIngredientsAsync"/> — ingredients confirmed free of a comma-separated allergen list,
    ///     with configurable exclusion of may-contain and cross-contact-risk rows.
    /// </summary>
    public class AllergenAnalyticsService : IAllergenAnalyticsService
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<AllergenAnalyticsService> _logger;

        public AllergenAnalyticsService(
            PlaterlyDbContext db,
            ILogger<AllergenAnalyticsService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Returns platform-wide allergen prevalence data: count and percentage of users with each
        /// registered allergen, broken down by severity (Low/Medium/High) and trace inclusion.
        /// Results are ordered by user count descending.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>An <see cref="AllergenPrevalenceResponse"/> with per-allergen statistics.</returns>
        public async Task<AllergenPrevalenceResponse> GetPrevalenceAsync(
            CancellationToken ct = default)
        {
            var totalUsers = await _db.Users.CountAsync(ct);
            var totalUsersWithAllergies = await _db.UserAllergies
                .Where(ua => ua.IsEnabled)
                .Select(ua => ua.UserId)
                .Distinct()
                .CountAsync(ct);

            var allergens = await _db.Allergies
                .Select(a => new AllergenPrevalenceDto
                {
                    AllergyId = a.Id,
                    AllergyName = a.Name,
                    UserCount = a.UserAllergies.Count(ua => ua.IsEnabled),
                    Percentage = totalUsers > 0
                        ? Math.Round((decimal)a.UserAllergies.Count(ua => ua.IsEnabled) / totalUsers * 100, 2)
                        : 0,
                    LowSeverityCount = a.UserAllergies.Count(ua => ua.IsEnabled && ua.MinSeverity == AllergenSeverity.Low),
                    MediumSeverityCount = a.UserAllergies.Count(ua => ua.IsEnabled && ua.MinSeverity == AllergenSeverity.Medium),
                    HighSeverityCount = a.UserAllergies.Count(ua => ua.IsEnabled && ua.MinSeverity == AllergenSeverity.High),
                    IncludeTraceCount = a.UserAllergies.Count(ua => ua.IsEnabled && ua.IncludeTrace)
                })
                .OrderByDescending(a => a.UserCount)
                .ToListAsync(ct);

            return new AllergenPrevalenceResponse
            {
                Allergens = allergens,
                TotalUsersWithAllergies = totalUsersWithAllergies,
                TotalUsers = totalUsers
            };
        }

        /// <summary>
        /// Returns a paginated list of global ingredients that contain a given allergen type.
        /// Supports optional severity filtering (Low/Medium/High).
        /// </summary>
        /// <param name="request">Allergen type, optional severity filter, and take count.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Matching ingredients with allergen metadata, ordered alphabetically by name.</returns>
        public async Task<AllergenIngredientsResponse> GetIngredientsByAllergenAsync(
            AllergenIngredientsRequest request,
            CancellationToken ct = default)
        {
            var query = _db.GlobalIngredientAllergens
                .Include(ga => ga.GlobalIngredient)
                    .ThenInclude(g => g.Category)
                .Where(ga => ga.AllergenType.ToLower() == request.AllergenType.ToLower());

            if (!string.IsNullOrEmpty(request.SeverityFilter) &&
                Enum.TryParse<AllergenSeverity>(request.SeverityFilter, true, out var severity))
            {
                query = query.Where(ga => ga.Severity == severity);
            }

            var totalCount = await query.CountAsync(ct);

            var ingredients = await query
                .OrderBy(ga => ga.GlobalIngredient.Name)
                .Take(request.Take)
                .Select(ga => new AllergenIngredientDto
                {
                    GlobalIngredientId = ga.GlobalIngredientId,
                    IngredientName = ga.GlobalIngredient.Name,
                    Category = ga.GlobalIngredient.Category != null ? ga.GlobalIngredient.Category.Name : null,
                    AllergenType = ga.AllergenType,
                    Severity = ga.Severity.ToString(),
                    MayContain = ga.MayContain,
                    CrossContactRisk = ga.CrossContactRisk,
                    ProcessingAid = ga.ProcessingAid,
                    ENumber = ga.ENumber,
                    Notes = ga.Notes
                })
                .ToListAsync(ct);

            return new AllergenIngredientsResponse
            {
                AllergenType = request.AllergenType,
                Ingredients = ingredients,
                TotalIngredients = totalCount
            };
        }

        /// <summary>
        /// Returns ingredients that have no confirmed allergen rows for the specified allergen list.
        /// Supports optional exclusion of may-contain and cross-contact-risk entries, and optional
        /// filtering by ingredient category slug.
        /// </summary>
        /// <param name="request">Comma-separated allergen list, exclusion flags, category filter, and take count.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Safe ingredients with total count.</returns>
        public async Task<SafeIngredientsResponse> GetSafeIngredientsAsync(
            SafeIngredientsRequest request,
            CancellationToken ct = default)
        {
            var allergenList = request.Allergens
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(a => a.ToLower())
                .ToList();

            if (allergenList.Count == 0)
            {
                return new SafeIngredientsResponse
                {
                    ExcludedAllergens = new(),
                    Ingredients = new(),
                    TotalSafeIngredients = 0
                };
            }

            // Find ingredient IDs that have any of the specified allergens
            var unsafeQuery = _db.GlobalIngredientAllergens
                .Where(ga => allergenList.Contains(ga.AllergenType.ToLower()));

            if (request.ExcludeMayContain)
            {
                // Include may-contain in the unsafe set
            }
            else
            {
                // Only exclude ingredients with definite allergens (not may-contain)
                unsafeQuery = unsafeQuery.Where(ga => !ga.MayContain);
            }

            if (request.ExcludeCrossContact)
            {
                // Cross-contact is already in the unsafe set by default
            }
            else
            {
                unsafeQuery = unsafeQuery.Where(ga => !ga.CrossContactRisk);
            }

            var unsafeIngredientIds = await unsafeQuery
                .Select(ga => ga.GlobalIngredientId)
                .Distinct()
                .ToListAsync(ct);

            var safeQuery = _db.GlobalIngredients
                .Include(g => g.Category)
                .Where(g => !unsafeIngredientIds.Contains(g.Id));

            if (!string.IsNullOrEmpty(request.Category))
            {
                safeQuery = safeQuery.Where(g => g.Category != null &&
                    g.Category.Slug == request.Category);
            }

            var totalSafe = await safeQuery.CountAsync(ct);

            var safeIngredients = await safeQuery
                .OrderBy(g => g.Name)
                .Take(request.Take)
                .Select(g => new SafeIngredientDto
                {
                    GlobalIngredientId = g.Id,
                    IngredientName = g.Name,
                    Category = g.Category != null ? g.Category.Name : null
                })
                .ToListAsync(ct);

            return new SafeIngredientsResponse
            {
                ExcludedAllergens = allergenList,
                Ingredients = safeIngredients,
                TotalSafeIngredients = totalSafe,
                ExcludedMayContain = request.ExcludeMayContain,
                ExcludedCrossContact = request.ExcludeCrossContact
            };
        }
    }
}
