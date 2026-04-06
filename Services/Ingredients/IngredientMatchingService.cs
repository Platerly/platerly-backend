using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Ingredients
{
    /// <inheritdoc />
    public class IngredientMatchingService : IIngredientMatchingService
    {
        private const int MinWordDropLength = 3;

        private readonly PlaterlyDbContext _db;

        public IngredientMatchingService(PlaterlyDbContext db)
        {
            _db = db;
        }

        /// <inheritdoc />
        public async Task<Dictionary<string, GlobalIngredient>> ResolveAsync(
            IEnumerable<string> normalizedNames,
            CancellationToken ct = default)
        {
            var names = normalizedNames.Distinct().ToList();
            if (names.Count == 0)
                return new Dictionary<string, GlobalIngredient>();

            var lookup = new Dictionary<string, GlobalIngredient>(StringComparer.Ordinal);

            // ── Level 1: Exact NormalizedName match ─────────────────────────────
            var exactMatches = await _db.GlobalIngredients
                .AsNoTracking()
                .Where(gi => gi.IsActive && names.Contains(gi.NormalizedName))
                .ToListAsync(ct);

            foreach (var gi in exactMatches)
                lookup[gi.NormalizedName] = gi;

            // ── Level 2: Alias table lookup (SQL index, no full-table scan) ─────
            var unmatched = names.Except(lookup.Keys).ToList();
            if (unmatched.Count > 0)
            {
                var aliasMatches = await _db.GlobalIngredientAliases
                    .AsNoTracking()
                    .Where(a => unmatched.Contains(a.NormalizedAlias))
                    .Select(a => new { a.NormalizedAlias, a.GlobalIngredient })
                    .ToListAsync(ct);

                foreach (var row in aliasMatches)
                {
                    if (!lookup.ContainsKey(row.NormalizedAlias))
                        lookup[row.NormalizedAlias] = row.GlobalIngredient;
                }
            }

            // ── Level 3: Progressive word-drop fuzzy match ───────────────────────
            // "extra-lean ground beef" → candidates: "lean ground beef", "ground beef", "beef"
            // Bulk-fetch all candidates in one query; shortest matching suffix wins.
            var stillUnmatched = names.Except(lookup.Keys).ToList();
            if (stillUnmatched.Count > 0)
            {
                // candidate normalized name → original input name
                var wordDropMap = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var name in stillUnmatched)
                {
                    var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 1; i < words.Length; i++)
                    {
                        var candidate = string.Join(" ", words.Skip(i));
                        if (candidate.Length >= MinWordDropLength && !wordDropMap.ContainsKey(candidate))
                            wordDropMap[candidate] = name;
                    }
                }

                if (wordDropMap.Count > 0)
                {
                    var candidateKeys = wordDropMap.Keys.ToList();
                    var wordDropMatches = await _db.GlobalIngredients
                        .AsNoTracking()
                        .Where(gi => gi.IsActive && candidateKeys.Contains(gi.NormalizedName))
                        .ToListAsync(ct);

                    // Prefer shorter suffix (more specific match) — stop at first hit per original name
                    foreach (var gi in wordDropMatches)
                    {
                        var originalName = wordDropMap[gi.NormalizedName];
                        if (!lookup.ContainsKey(originalName))
                            lookup[originalName] = gi;
                    }
                }
            }

            return lookup;
        }
    }
}
