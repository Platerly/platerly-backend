using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platerly.Api.Data;
using Platerly.Api.Models;

namespace Platerly.Api.Services
{
    /// <summary>
    /// Background job: recompute Trending once at app startup (after a short delay).
    /// You can run it on a timer if you want, but for now it fires once.
    /// </summary>
    public class TrendingRecomputeService : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<TrendingRecomputeService> _log;

        // Tunables
        private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
        private static readonly int CreatedWindowDays = 180;  // widen from 30 -> 180
        private static readonly int InteractionWindowDays = 30;
        private static readonly int CacheTake = 100;          // how many to store
        private const bool EnableNeverEmptyFallback = true;   // set false to disable

        public TrendingRecomputeService(IServiceProvider sp, ILogger<TrendingRecomputeService> log)
        {
            _sp = sp;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(StartupDelay, stoppingToken);
                await RecomputeOnce(stoppingToken);
            }
            catch (TaskCanceledException) { /* shutting down */ }
            catch (Exception ex)
            {
                _log.LogError(ex, "TrendingRecomputeService failed on startup run.");
            }
        }

        public async Task RecomputeOnce(CancellationToken ct = default)
        {
            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PlaterlyDbContext>();

            var now = DateTime.UtcNow;
            var createdCutoff = now.AddDays(-CreatedWindowDays);
            var interactCutoff = now.AddDays(-InteractionWindowDays);

            // ---------- Candidate Sets ----------
            // Base: recipes created in last 180 days and not deleted
            var recentCreatedQ = db.Recipes.AsNoTracking()
                .Where(r => !r.IsDeleted && r.CreatedAt >= createdCutoff)
                .Select(r => r.Id);

            // Interactions in last 30 days (likes + reposts)
            // Adjust table names if yours differ.
            var likedQ = db.RecipeLikes.AsNoTracking()
                .Where(l => l.LikedAt >= interactCutoff)
                .Select(l => l.RecipeId);

            var repostQ = db.RecipeReposts.AsNoTracking()
                .Where(p => p.RepostedAt >= interactCutoff)
                .Select(p => p.RecipeId);

            // “Remix” = child recipe created in last 30d; attribute to the origin (ParentRecipeId)
            var remixOriginQ = db.Recipes.AsNoTracking()
                .Where(r => !r.IsDeleted && r.ParentRecipeId != null && r.CreatedAt >= interactCutoff)
                .Select(r => r.ParentRecipeId!.Value);

            // Union of candidates
            var candidateIdsQ = recentCreatedQ
                .Union(likedQ)
                .Union(repostQ)
                .Union(remixOriginQ);

            var candidateIds = await candidateIdsQ.Distinct().ToListAsync(ct);

            // If absolutely no candidates found, DO NOT wipe cache.
            // Optionally fall back to recent-most list when cache empty.
            bool hasExistingCache = await db.RecipeTrendingCache.AnyAsync(ct);
            if (candidateIds.Count == 0)
            {
                _log.LogInformation("Trending: no candidates found (created<=180d or interactions<=30d).");
                if (!EnableNeverEmptyFallback)
                {
                    _log.LogInformation("Trending: not touching cache (fallback disabled).");
                    return;
                }

                if (!hasExistingCache)
                {
                    _log.LogInformation("Trending: cache empty, using 'never empty' fallback to most recent 20.");
                    var recent = await db.Recipes.AsNoTracking()
                        .Where(r => !r.IsDeleted)
                        .OrderByDescending(r => r.CreatedAt)
                        .Take(20)
                        .Select(r => new RecipeTrendingCache
                        {
                            RecipeId = r.Id,
                            Score = 0,  // neutral
                            Rank = 0,
                            ComputedAt = now
                        })
                        .ToListAsync(ct);

                    if (recent.Count == 0)
                    {
                        _log.LogWarning("Trending: fallback found 0 recipes. Doing nothing.");
                        return;
                    }

                    // Replace cache via DELETE (not TRUNCATE)
                    using var tx2 = await db.Database.BeginTransactionAsync(ct);
                    await db.Database.ExecuteSqlRawAsync("DELETE FROM [RecipeTrendingCache];", ct);
                    db.RecipeTrendingCache.AddRange(recent);
                    await db.SaveChangesAsync(ct);

                    // normalize ranks
                    var rows = await db.RecipeTrendingCache
                        .OrderByDescending(x => x.Score)
                        .ThenByDescending(x => x.ComputedAt)
                        .ToListAsync(ct);
                    for (int i = 0; i < rows.Count; i++) rows[i].Rank = i + 1;
                    await db.SaveChangesAsync(ct);
                    await tx2.CommitAsync(ct);

                    _log.LogInformation("Trending: fallback cache populated with {Count} most recent.", recent.Count);
                }

                return;
            }

            // ---------- Build scoring material ----------
            var set = candidateIds.ToHashSet();

            // Load minimal data needed to score
            var recipes = await db.Recipes.AsNoTracking()
                .Where(r => set.Contains(r.Id) && !r.IsDeleted)
                .Select(r => new { r.Id, r.CreatedAt })
                .ToListAsync(ct);

            // Interaction counts (last 30d)
            var likeCounts = await db.RecipeLikes.AsNoTracking()
                .Where(l => l.LikedAt >= interactCutoff && set.Contains(l.RecipeId))
                .GroupBy(l => l.RecipeId)
                .Select(g => new { RecipeId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RecipeId, x => x.Count, ct);

            var repostCounts = await db.RecipeReposts.AsNoTracking()
                .Where(p => p.RepostedAt >= interactCutoff && set.Contains(p.RecipeId))
                .GroupBy(p => p.RecipeId)
                .Select(g => new { RecipeId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RecipeId, x => x.Count, ct);

            // Remix counts attributed to origin (children in last 30d)
            var remixCounts = await db.Recipes.AsNoTracking()
                .Where(r => r.ParentRecipeId != null
                            && r.CreatedAt >= interactCutoff
                            && set.Contains(r.ParentRecipeId.Value))
                .GroupBy(r => r.ParentRecipeId!.Value)
                .Select(g => new { OriginId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.OriginId, x => x.Count, ct);

            // ---------- Score ----------
            // Simple, transparent score: weighted interactions + freshness
            // Freshness boosts newer items but allows older items to show if active.
            List<RecipeTrendingCache> ranked = new();
            foreach (var r in recipes)
            {
                var ageDays = Math.Max(0, (now - r.CreatedAt).TotalDays);
                var likes = likeCounts.TryGetValue(r.Id, out var lc) ? lc : 0;
                var reposts = repostCounts.TryGetValue(r.Id, out var rc) ? rc : 0;
                var remixes = remixCounts.TryGetValue(r.Id, out var mc) ? mc : 0;

                // weights: tweak as needed
                double interactionScore = likes * 3 + reposts * 4 + remixes * 6;

                // freshness: 30 is the “fresh horizon”; older stuff decays gradually
                double freshnessBoost = Math.Max(0, 30 - ageDays); // 0..30

                double score = interactionScore + freshnessBoost;

                ranked.Add(new RecipeTrendingCache
                {
                    RecipeId = r.Id,
                    Score = score,
                    Rank = 0,
                    ComputedAt = now
                });
            }

            ranked = ranked
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.ComputedAt)
                .Take(CacheTake)
                .ToList();

            // ---------- Optional “never empty” tail ----------
            if (ranked.Count == 0 && EnableNeverEmptyFallback)
            {
                _log.LogInformation("Trending: ranked empty; considering fallback because cache {State}.",
                    hasExistingCache ? "exists" : "empty");

                if (!hasExistingCache)
                {
                    var recent = await db.Recipes.AsNoTracking()
                        .Where(r => !r.IsDeleted)
                        .OrderByDescending(r => r.CreatedAt)
                        .Take(20)
                        .Select(r => new RecipeTrendingCache
                        {
                            RecipeId = r.Id,
                            Score = 0,
                            Rank = 0,
                            ComputedAt = now
                        })
                        .ToListAsync(ct);

                    ranked = recent;
                }
            }

            // If still nothing, do nothing (keep old cache)
            if (ranked.Count == 0)
            {
                _log.LogInformation("Trending: nothing to write; preserving existing cache.");
                return;
            }

            // ---------- Swap cache (DELETE + insert) ----------
            using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM [RecipeTrendingCache];", ct);
            db.RecipeTrendingCache.AddRange(ranked);
            await db.SaveChangesAsync(ct);

            // normalize ranks
            var finalRows = await db.RecipeTrendingCache
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.ComputedAt)
                .ToListAsync(ct);
            for (int i = 0; i < finalRows.Count; i++) finalRows[i].Rank = i + 1;
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);

            _log.LogInformation("Trending: recompute wrote {Count} rows at {When:u}.", finalRows.Count, now);
        }
    }
}
