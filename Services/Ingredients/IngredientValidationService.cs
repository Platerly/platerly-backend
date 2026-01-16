using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Platerly.Api.Data;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Ingredients
{
    public class IngredientValidationService : IIngredientValidationService
    {
        private readonly PlaterlyDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly ILogger<IngredientValidationService> _logger;

        private const string CacheKey = "ProhibitedIngredients";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

        public IngredientValidationService(
            PlaterlyDbContext db,
            IMemoryCache cache,
            ILogger<IngredientValidationService> logger)
        {
            _db = db;
            _cache = cache;
            _logger = logger;
        }

        public async Task<IngredientValidationResult> ValidateIngredientsAsync(
            IEnumerable<string> ingredientNames,
            CancellationToken ct = default)
        {
            var violations = new List<IngredientViolation>();

            foreach (var name in ingredientNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;

                var match = await FindProhibitedMatchAsync(name, ct);
                if (match != null)
                {
                    violations.Add(new IngredientViolation
                    {
                        IngredientName = name,
                        MatchedKeyword = match.Keyword,
                        Category = match.Category.ToString(),
                        Reason = match.Reason ?? GetDefaultReason(match.Category),
                        Severity = match.Severity.ToString()
                    });
                }
            }

            return violations.Count > 0
                ? IngredientValidationResult.Failed(violations)
                : IngredientValidationResult.Success();
        }

        public async Task<IngredientValidationResult> ValidateIngredientAsync(
            string ingredientName,
            CancellationToken ct = default)
        {
            return await ValidateIngredientsAsync(new[] { ingredientName }, ct);
        }

        public async Task<ProhibitedIngredient?> FindProhibitedMatchAsync(
            string ingredientName,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(ingredientName))
                return null;

            var prohibitedList = await GetProhibitedIngredientsAsync(ct);
            var normalizedName = NormalizeName(ingredientName);

            foreach (var prohibited in prohibitedList)
            {
                if (IsMatch(normalizedName, prohibited))
                {
                    _logger.LogInformation(
                        "Ingredient '{Ingredient}' matched prohibited keyword '{Keyword}' (Category: {Category})",
                        ingredientName, prohibited.Keyword, prohibited.Category);
                    return prohibited;
                }
            }

            return null;
        }

        public async Task RefreshCacheAsync(CancellationToken ct = default)
        {
            _cache.Remove(CacheKey);
            await GetProhibitedIngredientsAsync(ct);
            _logger.LogInformation("Prohibited ingredients cache refreshed");
        }

        private async Task<List<ProhibitedIngredient>> GetProhibitedIngredientsAsync(CancellationToken ct)
        {
            if (_cache.TryGetValue(CacheKey, out List<ProhibitedIngredient>? cached) && cached != null)
            {
                return cached;
            }

            var list = await _db.ProhibitedIngredients
                .Where(p => p.IsActive)
                .AsNoTracking()
                .ToListAsync(ct);

            _cache.Set(CacheKey, list, CacheDuration);
            _logger.LogDebug("Loaded {Count} prohibited ingredients into cache", list.Count);

            return list;
        }

        private static string NormalizeName(string name)
        {
            // Lowercase, trim, remove extra spaces
            return Regex.Replace(name.ToLowerInvariant().Trim(), @"\s+", " ");
        }

        private static bool IsMatch(string normalizedIngredient, ProhibitedIngredient prohibited)
        {
            var keyword = prohibited.Keyword.ToLowerInvariant();

            return prohibited.MatchType switch
            {
                Models.MatchType.Exact => normalizedIngredient == keyword,
                Models.MatchType.Contains => normalizedIngredient.Contains(keyword),
                Models.MatchType.StartsWith => normalizedIngredient.StartsWith(keyword),
                Models.MatchType.Regex => TryRegexMatch(normalizedIngredient, keyword),
                _ => normalizedIngredient.Contains(keyword)
            };
        }

        private static bool TryRegexMatch(string input, string pattern)
        {
            try
            {
                return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static string GetDefaultReason(ProhibitedCategory category)
        {
            return category switch
            {
                ProhibitedCategory.Haram => "This ingredient is not halal and cannot be used on Platerly.",
                ProhibitedCategory.Dangerous => "This item is dangerous and not safe for consumption.",
                ProhibitedCategory.Illegal => "This item is illegal and cannot be used.",
                ProhibitedCategory.Harmful => "This item could cause harm and is not allowed.",
                ProhibitedCategory.Offensive => "This content is not appropriate for our platform.",
                _ => "This ingredient is not allowed on Platerly."
            };
        }
    }
}
