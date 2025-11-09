using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platerly.Api.Data;
using Platerly.Api.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Platerly.Api.Services
{
    /// <summary>
    /// Recomputes the global Trending cache every 15 minutes.
    /// Signals: 7d likes/reposts/remixes + 24h “flash”; age decay; fairness via follower count.
    /// Writes to RecipeTrendingCache (Score + Rank).
    /// </summary>
    public class TrendingRecomputeService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TrendingRecomputeService> _log;
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

        public TrendingRecomputeService(
            IServiceScopeFactory scopeFactory,
            ILogger<TrendingRecomputeService> log)
        {
            _scopeFactory = scopeFactory;
            _log = log;
        }

        /// <summary>
        /// Expose a one-off recompute you can call from an admin endpoint if desired.
        /// </summary>
        public Task RecomputeNow(CancellationToken ct) => RecomputeOnce(ct);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // small delay to let the app warm up and DB connections settle
            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); } catch { }

            // ✅ Run once at startup so cache is immediately available after deploy/restart
            try
            {
                await RecomputeOnce(stoppingToken);
                _log.LogInformation("Trending recompute (startup) finished.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _log.LogError(ex, "Trending recompute (startup) failed");
            }

            // Then loop on interval
            while (!stoppingToken.IsCancellationRequested)
            {
                var started = DateTime.UtcNow;
                try
                {
                    await RecomputeOnce(stoppingToken);
                    var ms = (DateTime.UtcNow - started).TotalMilliseconds;
                    _log.LogInformation("Trending recompute finished in {Ms} ms", ms);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // shutting down
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Trending recompute failed");
                }

                try { await Task.Delay(Interval, stoppingToken); } catch { }
            }
        }

        private async Task RecomputeOnce(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PlaterlyDbContext>();

            var now = DateTime.UtcNow;
            var since7 = now.AddDays(-7);
            var since1 = now.AddDays(-1);
            var since30 = now.AddDays(-30); // candidate window (keeps job fast; older items decay to ~0)

            // ── Candidate recipes (not deleted, last 30 days) ──
            // If your prod model uses CreatedAtUtc, change both CreatedAt references below to CreatedAtUtc.
            var candidates = await db.Recipes
                .AsNoTracking()
                .Where(r => !r.IsDeleted && r.CreatedAt >= since30)               // ← CHANGE to r.CreatedAtUtc if needed
                .Select(r => new { r.Id, r.UserId, r.CreatedAt })                 // ← CHANGE to r.CreatedAtUtc if needed
                .ToListAsync(ct);

            _log.LogInformation("Trending candidates (last 30d): {Count}", candidates.Count);

            if (candidates.Count == 0)
            {
                // nothing to rank; clear cache so FE doesn’t show stale
                await db.Database.ExecuteSqlRawAsync("DELETE FROM [RecipeTrendingCache];", ct);
                _log.LogInformation("Trending cache cleared (no candidates).");
                return;
            }

            var recipeIds = candidates.Select(c => c.Id).ToArray();
            var byId = candidates.ToDictionary(c => c.Id, c => c);

            // ── Aggregates (7d / 1d) ──
            var l7Likes = await db.RecipeLikes.AsNoTracking()
                .Where(x => x.LikedAt >= since7 && recipeIds.Contains(x.RecipeId))
                .GroupBy(x => x.RecipeId)
                .Select(g => new { RecipeId = g.Key, C = g.Count() })
                .ToDictionaryAsync(x => x.RecipeId, x => x.C, ct);

            var d1Likes = await db.RecipeLikes.AsNoTracking()
                .Where(x => x.LikedAt >= since1 && recipeIds.Contains(x.RecipeId))
                .GroupBy(x => x.RecipeId)
                .Select(g => new { RecipeId = g.Key, C = g.Count() })
                .ToDictionaryAsync(x => x.RecipeId, x => x.C, ct);

            var l7Reposts = await db.RecipeReposts.AsNoTracking()
                .Where(x => x.RepostedAt >= since7 && recipeIds.Contains(x.RecipeId))
                .GroupBy(x => x.RecipeId)
                .Select(g => new { RecipeId = g.Key, C = g.Count() })
                .ToDictionaryAsync(x => x.RecipeId, x => x.C, ct);

            var d1Reposts = await db.RecipeReposts.AsNoTracking()
                .Where(x => x.RepostedAt >= since1 && recipeIds.Contains(x.RecipeId))
                .GroupBy(x => x.RecipeId)
                .Select(g => new { RecipeId = g.Key, C = g.Count() })
                .ToDictionaryAsync(x => x.RecipeId, x => x.C, ct);

            // Remixes are stored with ParentRecipeId = the recipe being remixed
            var l7Remixes = await db.RecipeRemixes.AsNoTracking()
                .Where(x => x.RemixedAt >= since7 && x.ParentRecipeId != null && recipeIds.Contains(x.ParentRecipeId.Value))
                .GroupBy(x => x.ParentRecipeId!.Value)
                .Select(g => new { RecipeId = g.Key, C = g.Count() })
                .ToDictionaryAsync(x => x.RecipeId, x => x.C, ct);

            var d1Remixes = await db.RecipeRemixes.AsNoTracking()
                .Where(x => x.RemixedAt >= since1 && x.ParentRecipeId != null && recipeIds.Contains(x.ParentRecipeId.Value))
                .GroupBy(x => x.ParentRecipeId!.Value)
                .Select(g => new { RecipeId = g.Key, C = g.Count() })
                .ToDictionaryAsync(x => x.RecipeId, x => x.C, ct);

            // ── Fairness via follower count (by author) ──
            var authorIds = candidates.Select(c => c.UserId).Distinct().ToArray();
            var followerCounts = await db.UserFollows.AsNoTracking()
                .Where(f => authorIds.Contains(f.FollowingId))
                .GroupBy(f => f.FollowingId)
                .Select(g => new { AuthorId = g.Key, Followers = g.Count() })
                .ToDictionaryAsync(x => x.AuthorId, x => x.Followers, ct);

            // ── Tunables ──
            const double W_L7_LIKES = 1.0;
            const double W_L7_REPOSTS = 1.2;
            const double W_L7_REMIXES = 2.0;

            const double W_D1_LIKES = 0.5;
            const double W_D1_REPOSTS = 0.6;
            const double W_D1_REMIXES = 0.9;

            const double DECAY_HALF_LIFE_HOURS = 36.0; // timeDecay = 1 / (1 + ageH/36)
            const double CREATOR_DAMP_DENOM = 1.0;  // increase to damp big creators more

            var scores = new List<(Guid RecipeId, double Score)>(recipeIds.Length);

            foreach (var rid in recipeIds)
            {
                var meta = byId[rid];

                var baseMomentum =
                    W_L7_LIKES * (l7Likes.TryGetValue(rid, out var n1) ? n1 : 0) +
                    W_L7_REPOSTS * (l7Reposts.TryGetValue(rid, out var n2) ? n2 : 0) +
                    W_L7_REMIXES * (l7Remixes.TryGetValue(rid, out var n3) ? n3 : 0);

                var flash =
                    W_D1_LIKES * (d1Likes.TryGetValue(rid, out var m1) ? m1 : 0) +
                    W_D1_REPOSTS * (d1Reposts.TryGetValue(rid, out var m2) ? m2 : 0) +
                    W_D1_REMIXES * (d1Remixes.TryGetValue(rid, out var m3) ? m3 : 0);

                // If your model uses CreatedAtUtc, change meta.CreatedAt to CreatedAtUtc here:
                var ageHours = (now - meta.CreatedAt).TotalHours;   // ← CHANGE to meta.CreatedAtUtc if needed
                var timeDecay = 1.0 / (1.0 + (ageHours / DECAY_HALF_LIFE_HOURS));

                var followers = followerCounts.TryGetValue(meta.UserId, out var fc) ? fc : 0;
                var creatorBoost = 1.0 / (1.0 + (Math.Log10(1.0 + followers) / CREATOR_DAMP_DENOM));

                var score = (baseMomentum + flash) * timeDecay * creatorBoost;
                if (double.IsNaN(score) || double.IsInfinity(score)) score = 0;

                scores.Add((rid, score));
            }

            var ranked = scores
                .OrderByDescending(s => s.Score)
                .Select((s, idx) => new RecipeTrendingCache
                {
                    RecipeId = s.RecipeId,
                    Score = s.Score,
                    Rank = idx + 1,
                    ComputedAt = now
                })
                .ToList();

            using var tx = await db.Database.BeginTransactionAsync(ct);

            // ✅ Use DELETE (safe with Azure SQL perms & inside a transaction)
            await db.Database.ExecuteSqlRawAsync("DELETE FROM [RecipeTrendingCache];", ct);

            if (ranked.Count > 0)
            {
                await db.RecipeTrendingCache.AddRangeAsync(ranked, ct);
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _log.LogInformation("Trending wrote {Count} rows at {At:o}", ranked.Count, now);
        }
    }
}
