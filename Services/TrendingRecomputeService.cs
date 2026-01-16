using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platerly.Api.Data;
using Platerly.Api.Models;
using Platerly.Api.Services.Notifications;

namespace Platerly.Api.Services
{
    /// <summary>
    /// Background job: recomputes Trending periodically like a production-grade job.
    /// Runs once shortly after startup, then hourly forever with jitter and concurrency safety.
    ///
    /// Enhanced algorithm includes:
    /// - Views, comments, and cookbook saves in scoring
    /// - Velocity scoring (engagement acceleration)
    /// - Author follower multiplier
    /// - Rank delta tracking
    /// </summary>
    public class TrendingRecomputeService : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<TrendingRecomputeService> _log;
        private readonly TrendingConfig _config;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public TrendingRecomputeService(
            IServiceProvider sp,
            ILogger<TrendingRecomputeService> log,
            IOptions<TrendingConfig> config)
        {
            _sp = sp;
            _log = log;
            _config = config.Value;
        }

        // --- Main background loop ---
        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            // random jitter to avoid thundering herd on multiple instances
            var jitterMs = Random.Shared.Next(
                _config.StartupJitterMinSeconds * 1000,
                _config.StartupJitterMaxSeconds * 1000
            );
            await Task.Delay(jitterMs, ct);

            // immediate first run
            await RunOnceSafe(ct);

            // stable cadence (no drift)
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_config.IntervalMinutes));
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
            var createdCutoff = now.AddDays(-_config.CreatedWindowDays);
            var interactCutoff = now.AddDays(-_config.InteractionWindowDays);
            var velocityCutoff24h = now.AddHours(-24);
            var velocityCutoff48h = now.AddHours(-48);

            // ---------- Get Previous Rankings (for delta calculation) ----------
            var previousRankings = await db.RecipeTrendingCache.AsNoTracking()
                .ToDictionaryAsync(x => x.RecipeId, x => x.Rank, ct);

            // ---------- Save History Snapshot ----------
            if (previousRankings.Count > 0)
            {
                var historySnapshot = await db.RecipeTrendingCache.AsNoTracking()
                    .Select(x => new RecipeTrendingHistory
                    {
                        RecipeId = x.RecipeId,
                        Score = x.Score,
                        Rank = x.Rank,
                        ComputedAt = x.ComputedAt
                    })
                    .ToListAsync(ct);

                db.RecipeTrendingHistory.AddRange(historySnapshot);
                await db.SaveChangesAsync(ct);

                // Cleanup old history (keep last 7 days)
                var historyCleanupCutoff = now.AddDays(-7);
                await db.RecipeTrendingHistory
                    .Where(h => h.ComputedAt < historyCleanupCutoff)
                    .ExecuteDeleteAsync(ct);
            }

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

            var commentedQ = db.Comments.AsNoTracking()
                .Where(c => c.CreatedAt >= interactCutoff)
                .Select(c => c.RecipeId);

            var viewedQ = db.RecipeViews.AsNoTracking()
                .Where(v => v.ViewedAt >= interactCutoff)
                .Select(v => v.RecipeId);

            var candidateIdsQ = recentCreatedQ
                .Union(likedQ)
                .Union(repostQ)
                .Union(remixOriginQ)
                .Union(commentedQ)
                .Union(viewedQ);

            var candidateIds = await candidateIdsQ.Distinct().ToListAsync(ct);

            bool hasExistingCache = await db.RecipeTrendingCache.AnyAsync(ct);
            if (candidateIds.Count == 0)
            {
                _log.LogInformation("Trending: no candidates (created<={CreatedDays}d or interacted<={InteractDays}d).", _config.CreatedWindowDays, _config.InteractionWindowDays);
                if (!_config.EnableNeverEmptyFallback)
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

            // ---------- Fetch Data for Scoring ----------
            var set = candidateIds.ToHashSet();

            // Recipe info + author
            var recipes = await db.Recipes.AsNoTracking()
                .Where(r => set.Contains(r.Id) && !r.IsDeleted)
                .Select(r => new { r.Id, r.CreatedAt, r.UserId })
                .ToListAsync(ct);

            var recipesByAuthor = recipes.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.Select(r => r.Id).ToList());
            var authorIds = recipesByAuthor.Keys.ToHashSet();

            // Author follower counts (for author boost)
            var authorFollowerCounts = await db.UserFollows.AsNoTracking()
                .Where(uf => authorIds.Contains(uf.FollowingId))
                .GroupBy(uf => uf.FollowingId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

            // Interaction counts (30 day window)
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

            var commentCounts = await db.Comments.AsNoTracking()
                .Where(c => c.CreatedAt >= interactCutoff && set.Contains(c.RecipeId))
                .GroupBy(c => c.RecipeId)
                .Select(g => new { RecipeId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RecipeId, x => x.Count, ct);

            var viewCounts = await db.RecipeViews.AsNoTracking()
                .Where(v => v.ViewedAt >= interactCutoff && set.Contains(v.RecipeId))
                .GroupBy(v => v.RecipeId)
                .Select(g => new { RecipeId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RecipeId, x => x.Count, ct);

            var saveCounts = await db.CookbookRecipes.AsNoTracking()
                .Where(cr => cr.AddedAt >= interactCutoff && set.Contains(cr.RecipeId))
                .GroupBy(cr => cr.RecipeId)
                .Select(g => new { RecipeId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RecipeId, x => x.Count, ct);

            // Velocity data: engagement in last 24h vs previous 24h
            var likes24h = await db.RecipeLikes.AsNoTracking()
                .Where(l => l.LikedAt >= velocityCutoff24h && set.Contains(l.RecipeId))
                .GroupBy(l => l.RecipeId)
                .Select(g => new { RecipeId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RecipeId, x => x.Count, ct);

            var likesPrev24h = await db.RecipeLikes.AsNoTracking()
                .Where(l => l.LikedAt >= velocityCutoff48h && l.LikedAt < velocityCutoff24h && set.Contains(l.RecipeId))
                .GroupBy(l => l.RecipeId)
                .Select(g => new { RecipeId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RecipeId, x => x.Count, ct);

            var views24h = await db.RecipeViews.AsNoTracking()
                .Where(v => v.ViewedAt >= velocityCutoff24h && set.Contains(v.RecipeId))
                .GroupBy(v => v.RecipeId)
                .Select(g => new { RecipeId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RecipeId, x => x.Count, ct);

            var viewsPrev24h = await db.RecipeViews.AsNoTracking()
                .Where(v => v.ViewedAt >= velocityCutoff48h && v.ViewedAt < velocityCutoff24h && set.Contains(v.RecipeId))
                .GroupBy(v => v.RecipeId)
                .Select(g => new { RecipeId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RecipeId, x => x.Count, ct);

            // ---------- Scoring ----------
            List<RecipeTrendingCache> ranked = new();
            foreach (var r in recipes)
            {
                var ageDays = Math.Max(0, (now - r.CreatedAt).TotalDays);

                // Get counts
                var likes = likeCounts.GetValueOrDefault(r.Id, 0);
                var reposts = repostCounts.GetValueOrDefault(r.Id, 0);
                var remixes = remixCounts.GetValueOrDefault(r.Id, 0);
                var comments = commentCounts.GetValueOrDefault(r.Id, 0);
                var views = viewCounts.GetValueOrDefault(r.Id, 0);
                var saves = saveCounts.GetValueOrDefault(r.Id, 0);

                // Calculate velocity multiplier
                var engagement24h = likes24h.GetValueOrDefault(r.Id, 0) + views24h.GetValueOrDefault(r.Id, 0);
                var engagementPrev24h = likesPrev24h.GetValueOrDefault(r.Id, 0) + viewsPrev24h.GetValueOrDefault(r.Id, 0);
                var velocityMultiplier = CalculateVelocity(engagement24h, engagementPrev24h);

                // Calculate author boost
                var authorFollowers = authorFollowerCounts.GetValueOrDefault(r.UserId, 0);
                var authorMultiplier = CalculateAuthorBoost(authorFollowers);

                // Base interaction score
                double interactionScore =
                    (likes * _config.LikeWeight) +
                    (reposts * _config.RepostWeight) +
                    (remixes * _config.RemixWeight) +
                    (comments * _config.CommentWeight) +
                    (views * _config.ViewWeight) +
                    (saves * _config.SaveWeight);

                // Apply multipliers and freshness
                double freshnessBoost = Math.Max(0, 30 - ageDays);
                double score = (interactionScore * authorMultiplier * velocityMultiplier) + freshnessBoost;

                // Get previous rank for delta
                var prevRank = previousRankings.GetValueOrDefault(r.Id, 0);

                ranked.Add(new RecipeTrendingCache
                {
                    RecipeId = r.Id,
                    Score = score,
                    Rank = 0,
                    PreviousRank = prevRank > 0 ? prevRank : null,
                    RankDelta = 0, // Will be calculated after sorting
                    VelocityScore = velocityMultiplier,
                    ViewCount = views,
                    CommentCount = comments,
                    SaveCount = saves,
                    LikeCount = likes,
                    RepostCount = reposts,
                    RemixCount = remixes,
                    ComputedAt = now
                });
            }

            ranked = ranked
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.ComputedAt)
                .Take(_config.CacheSize)
                .ToList();

            if (ranked.Count == 0)
            {
                _log.LogInformation("Trending: ranked empty, preserving cache.");
                return;
            }

            // Calculate rank deltas
            for (int i = 0; i < ranked.Count; i++)
            {
                ranked[i].Rank = i + 1;
                if (ranked[i].PreviousRank.HasValue && ranked[i].PreviousRank.Value > 0)
                {
                    // Positive delta = moving up (e.g., was #10, now #5 = delta of +5)
                    ranked[i].RankDelta = ranked[i].PreviousRank.Value - ranked[i].Rank;
                }
            }

            // ---------- Persist to Database ----------
            using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM [RecipeTrendingCache];", ct);
            db.RecipeTrendingCache.AddRange(ranked);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _log.LogInformation("Trending: wrote {Count} rows at {When:u}.", ranked.Count, now);

            // ---------- Send Notifications for Newly Trending Recipes ----------
            await SendTrendingNotifications(db, ranked, previousRankings, ct);
        }

        private static double CalculateVelocity(int engagement24h, int engagementPrev24h)
        {
            // If no previous engagement but some recent engagement, give a boost
            if (engagementPrev24h == 0)
            {
                return engagement24h > 5 ? 1.5 : 1.0;
            }

            double ratio = (double)engagement24h / engagementPrev24h;

            // Clamp between 0.5 and 2.0
            return Math.Clamp(ratio, 0.5, 2.0);
        }

        private static double CalculateAuthorBoost(int followerCount)
        {
            // Logarithmic scale to prevent mega-influencer dominance
            if (followerCount < 100) return 1.0;
            if (followerCount < 1000) return 1.1;
            if (followerCount < 10000) return 1.2;
            return 1.3; // cap at 30% boost
        }

        private async Task SendTrendingNotifications(
            PlaterlyDbContext db,
            List<RecipeTrendingCache> ranked,
            Dictionary<Guid, int> previousRankings,
            CancellationToken ct)
        {
            try
            {
                var notificationService = _sp.CreateScope().ServiceProvider.GetService<INotificationService>();
                if (notificationService == null)
                {
                    _log.LogWarning("NotificationService not available for trending notifications.");
                    return;
                }

                // Find recipes that entered top N (weren't in top N before)
                var newlyTrending = ranked
                    .Where(r => r.Rank <= _config.NotificationThreshold)
                    .Where(r => !previousRankings.TryGetValue(r.RecipeId, out var prev) || prev > _config.NotificationThreshold)
                    .ToList();

                if (newlyTrending.Count == 0) return;

                // Get recipe owner info
                var recipeIds = newlyTrending.Select(r => r.RecipeId).ToList();
                var recipeOwners = await db.Recipes.AsNoTracking()
                    .Where(r => recipeIds.Contains(r.Id))
                    .Select(r => new { r.Id, r.UserId })
                    .ToDictionaryAsync(r => r.Id, r => r.UserId, ct);

                foreach (var item in newlyTrending)
                {
                    if (recipeOwners.TryGetValue(item.RecipeId, out var ownerId))
                    {
                        await notificationService.NotifyRecipeTrendingAsync(ownerId, item.RecipeId, item.Rank, ct);
                        _log.LogInformation("Trending notification sent for recipe {RecipeId} at rank {Rank}", item.RecipeId, item.Rank);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to send trending notifications");
            }
        }
    }
}
