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
    /// Background job: recomputes Trending periodically like a production-grade job.
    /// Runs once shortly after startup, then hourly forever with jitter and concurrency safety.
    /// </summary>
    public class TrendingRecomputeService : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<TrendingRecomputeService> _log;
        private readonly SemaphoreSlim _gate = new(1, 1);

        // Tunables
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(60);  // hourly refresh
        private static readonly TimeSpan StartupJitterMin = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan StartupJitterMax = TimeSpan.FromSeconds(12);
        private static readonly int CreatedWindowDays = 180;
        private static readonly int InteractionWindowDays = 30;
        private static readonly int CacheTake = 100;
        private const bool EnableNeverEmptyFallback = true;

        public TrendingRecomputeService(IServiceProvider sp, ILogger<TrendingRecomputeService> log)
        {
            _sp = sp;
            _log = log;
        }

        // --- Main background loop ---
        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            // random jitter to avoid thundering herd on multiple instances
            var jitterMs = Random.Shared.Next(
                (int)StartupJitterMin.TotalMilliseconds,
                (int)StartupJitterMax.TotalMilliseconds
            );
            await Task.Delay(jitterMs, ct);

            // immediate first run
            await RunOnceSafe(ct);

            // stable cadence (no drift)
            using var timer = new PeriodicTimer(Interval);
            while (await timer.WaitForNextTickAsync(ct))
            {
                await RunOnceSafe(ct);
            }
        }

        // ensures single run at a time, safe logging
        private async Task RunOnceSafe(CancellationToken ct)
        {
            if (!await _gate.WaitAsync(0, ct))
            {
                _log.LogWarning("Trending recompute skipped: previous run still in progress.");
                return;
            }

            var started = DateTime.UtcNow;
            try
            {
                _log.LogInformation("Trending: recompute started at {Now:u}", started);
                await RecomputeOnce(ct);
                var elapsed = DateTime.UtcNow - started;
                _log.LogInformation("Trending: recompute completed in {Ms} ms", elapsed.TotalMilliseconds);
            }
            catch (TaskCanceledException) { /* graceful shutdown */ }
            catch (Exception ex)
            {
                _log.LogError(ex, "Trending recompute failed unexpectedly.");
            }
            finally
            {
                _gate.Release();
            }
        }

        // --- Core trending recomputation logic ---
        public async Task RecomputeOnce(CancellationToken ct = default)
        {
            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PlaterlyDbContext>();

            var now = DateTime.UtcNow;
            var createdCutoff = now.AddDays(-CreatedWindowDays);
            var interactCutoff = now.AddDays(-InteractionWindowDays);

            // ---------- Candidate Sets ----------
            var recentCreatedQ = db.Recipes.AsNoTracking()
                .Where(r => !r.IsDeleted && r.CreatedAt >= createdCutoff)
                .Select(r => r.Id);

            var likedQ = db.RecipeLikes.AsNoTracking()
                .Where(l => l.LikedAt >= interactCutoff)
                .Select(l => l.RecipeId);

            var repostQ = db.RecipeReposts.AsNoTracking()
                .Where(p => p.RepostedAt >= interactCutoff)
                .Select(p => p.RecipeId);

            var remixOriginQ = db.Recipes.AsNoTracking()
                .Where(r => !r.IsDeleted && r.ParentRecipeId != null && r.CreatedAt >= interactCutoff)
                .Select(r => r.ParentRecipeId!.Value);

            var candidateIdsQ = recentCreatedQ
                .Union(likedQ)
                .Union(repostQ)
                .Union(remixOriginQ);

            var candidateIds = await candidateIdsQ.Distinct().ToListAsync(ct);

            bool hasExistingCache = await db.RecipeTrendingCache.AnyAsync(ct);
            if (candidateIds.Count == 0)
            {
                _log.LogInformation("Trending: no candidates (created<=180d or interacted<=30d).");
                if (!EnableNeverEmptyFallback)
                    return;

                if (!hasExistingCache)
                {
                    _log.LogInformation("Trending: cache empty, seeding fallback list.");
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

                    if (recent.Count == 0)
                    {
                        _log.LogWarning("Trending: fallback found 0 recipes. Aborting.");
                        return;
                    }

                    using var tx2 = await db.Database.BeginTransactionAsync(ct);
                    await db.Database.ExecuteSqlRawAsync("DELETE FROM [RecipeTrendingCache];", ct);
                    db.RecipeTrendingCache.AddRange(recent);
                    await db.SaveChangesAsync(ct);
                    for (int i = 0; i < recent.Count; i++) recent[i].Rank = i + 1;
                    await db.SaveChangesAsync(ct);
                    await tx2.CommitAsync(ct);

                    _log.LogInformation("Trending: fallback cache seeded with {Count} items.", recent.Count);
                }

                return;
            }

            // ---------- Scoring ----------
            var set = candidateIds.ToHashSet();
            var recipes = await db.Recipes.AsNoTracking()
                .Where(r => set.Contains(r.Id) && !r.IsDeleted)
                .Select(r => new { r.Id, r.CreatedAt })
                .ToListAsync(ct);

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

            var remixCounts = await db.Recipes.AsNoTracking()
                .Where(r => r.ParentRecipeId != null
                            && r.CreatedAt >= interactCutoff
                            && set.Contains(r.ParentRecipeId.Value))
                .GroupBy(r => r.ParentRecipeId!.Value)
                .Select(g => new { OriginId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.OriginId, x => x.Count, ct);

            List<RecipeTrendingCache> ranked = new();
            foreach (var r in recipes)
            {
                var ageDays = Math.Max(0, (now - r.CreatedAt).TotalDays);
                var likes = likeCounts.TryGetValue(r.Id, out var lc) ? lc : 0;
                var reposts = repostCounts.TryGetValue(r.Id, out var rc) ? rc : 0;
                var remixes = remixCounts.TryGetValue(r.Id, out var mc) ? mc : 0;

                double interactionScore = likes * 3 + reposts * 4 + remixes * 6;
                double freshnessBoost = Math.Max(0, 30 - ageDays);
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

            if (ranked.Count == 0)
            {
                _log.LogInformation("Trending: ranked empty, preserving cache.");
                return;
            }

            using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM [RecipeTrendingCache];", ct);
            db.RecipeTrendingCache.AddRange(ranked);
            await db.SaveChangesAsync(ct);

            var finalRows = await db.RecipeTrendingCache
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.ComputedAt)
                .ToListAsync(ct);
            for (int i = 0; i < finalRows.Count; i++) finalRows[i].Rank = i + 1;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _log.LogInformation("Trending: wrote {Count} rows at {When:u}.", finalRows.Count, now);
        }
    }
}
