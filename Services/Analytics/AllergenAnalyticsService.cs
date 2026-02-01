using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Analytics
{
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
