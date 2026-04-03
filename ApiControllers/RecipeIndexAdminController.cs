using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;
using Platerly.Api.Services.RecipeIndex;
using System.ComponentModel.DataAnnotations;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// Admin-only controller for managing the Recipe Index Engine.
    ///
    /// Base route: /api/admin/recipe-index
    /// Auth requirement: [Authorize(Roles = "Admin,SuperAdmin")] — JWT with an Admin or SuperAdmin role claim.
    ///
    /// Pipeline management:
    ///   POST /run-analysis  — Manually fires the <see cref="IRecipeAnalysisPipelineService"/> in a
    ///                         background <see cref="System.Threading.Tasks.Task"/>. Returns 202 Accepted
    ///                         immediately; progress is visible via GET /stats. Each run only processes
    ///                         rows where <c>IsAnalysed = false</c>, so manual and scheduled runs
    ///                         can coexist without double-processing.
    ///   POST /reset-analysis — Marks analysed recipes as unanalysed so the pipeline will reprocess them.
    ///                          Use after a pipeline bug-fix to trigger a re-run on already-processed rows.
    ///                          Optionally scoped to a specific <c>AnalysisVersion</c> string.
    ///
    /// Domain / opt-out management:
    ///   GET    /blocked-domains               — lists all manually blocked or opt-out domains.
    ///   POST   /blocked-domains               — adds a blocked domain entry and deactivates its source.
    ///   DELETE /blocked-domains/{domain}      — removes a block (does not restore recipes).
    ///   DELETE /domains/{domain}/recipes      — removes all indexed recipes for a domain.
    ///   POST   /opt-out                       — atomic opt-out: removes recipes + blocks domain in one call.
    ///   PATCH  /recipes/{id}/image-blocked    — toggles <c>ImageDisplayBlocked</c> on a single recipe
    ///                                           to suppress thumbnail display without removing the recipe.
    ///
    /// GET  /stats — returns overall counts (total, analysed, pending, discarded) and
    ///               per-source domain breakdown including robots.txt status.
    /// </summary>
    [ApiController]
    [Route("api/admin/recipe-index")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class RecipeIndexAdminController : ControllerBase
    {
        private readonly PlaterlyDbContext _db;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RecipeIndexAdminController> _log;

        public RecipeIndexAdminController(
            PlaterlyDbContext db,
            IServiceScopeFactory scopeFactory,
            ILogger<RecipeIndexAdminController> log)
        {
            _db          = db;
            _scopeFactory = scopeFactory;
            _log         = log;
        }

        // ── GET /api/admin/recipe-index/stats ─────────────────────────────

        /// <summary>
        /// Returns overall recipe index statistics and a per-source-domain breakdown.
        ///
        /// Response includes total/analysed/pending/discarded counts, total blocked domains,
        /// and per-domain details: active status, last indexed at, robots.txt check result,
        /// and cumulative indexed/discarded counts.
        /// </summary>
        [HttpGet("stats")]
        public async Task<IActionResult> GetStats(CancellationToken ct = default)
        {
            var totalRecipes   = await _db.RecipeIndex.CountAsync(ct);
            var totalAnalysed  = await _db.RecipeIndex.CountAsync(r => r.IsAnalysed, ct);
            var totalPending   = totalRecipes - totalAnalysed;

            var sources = await _db.RecipeIndexSources
                .AsNoTracking()
                .OrderBy(s => s.SourceName)
                .ToListAsync(ct);

            var perDomain = sources.Select(s => new
            {
                s.Id,
                s.SourceName,
                s.SourceDomain,
                s.IsActive,
                s.LastIndexedAt,
                s.TotalRecipesIndexed,
                s.TotalRecipesDiscarded,
                RobotsStatus = s.RobotsAllowed == null  ? "Unchecked"
                             : s.RobotsAllowed == true  ? "Allowed"
                             : "Disallowed",
                s.RobotsCheckedAt,
                s.RobotsDisallowNote,
            }).ToList();

            var blockedCount = await _db.BlockedDomains.CountAsync(ct);

            return Ok(new
            {
                totalRecipes,
                totalAnalysed,
                totalPending,
                totalDiscarded = sources.Sum(s => s.TotalRecipesDiscarded),
                blockedDomains = blockedCount,
                domains = perDomain,
            });
        }

        // ── POST /api/admin/recipe-index/run-analysis ─────────────────────
        /// <summary>
        /// Manually triggers the analysis pipeline in the background.
        /// Returns immediately — check /stats for progress.
        /// Safe to call while the scheduled background run is also active;
        /// each run fetches only unanalysed rows so there's no double-processing.
        /// </summary>
        [HttpPost("run-analysis")]
        public IActionResult TriggerAnalysis()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope   = _scopeFactory.CreateScope();
                    var pipeline      = scope.ServiceProvider
                                            .GetRequiredService<IRecipeAnalysisPipelineService>();
                    var stats = await pipeline.RunAsync(CancellationToken.None);
                    _log.LogInformation(
                        "[RecipeIndexAdmin] Manual analysis run complete. " +
                        "Processed={Processed} Failed={Failed} Time={Time}ms",
                        stats.TotalProcessed, stats.TotalFailed, stats.ElapsedMs);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "[RecipeIndexAdmin] Manual analysis run failed.");
                }
            });

            _log.LogInformation("[RecipeIndexAdmin] Manual analysis pipeline triggered by admin.");
            return Accepted(new { message = "Analysis pipeline started. Check /stats for progress." });
        }

        // ── POST /api/admin/recipe-index/reset-analysis ───────────────────
        /// <summary>
        /// Marks all analysed recipes as unanalysed so the pipeline will re-process them.
        /// Use this after a pipeline bug fix to rerun analysis on already-processed rows.
        /// Optionally filter to only rows with a specific AnalysisVersion (e.g. "1.0" or "1.0-error").
        /// </summary>
        [HttpPost("reset-analysis")]
        public async Task<IActionResult> ResetAnalysis(
            [FromQuery] string? version = null,
            CancellationToken ct = default)
        {
            var query = _db.RecipeIndex.Where(r => r.IsAnalysed);

            if (!string.IsNullOrWhiteSpace(version))
                query = query.Where(r => r.AnalysisVersion == version);

            var rows = await query.ToListAsync(ct);

            foreach (var r in rows)
            {
                r.IsAnalysed      = false;
                r.AnalysisVersion = null;
                r.AllergenAnalysisJson = null;
                r.NickelLoadJson  = null;
            }

            await _db.SaveChangesAsync(ct);

            _log.LogInformation(
                "[RecipeIndexAdmin] Reset {Count} recipes to unanalysed (version filter: {Version}).",
                rows.Count, version ?? "all");

            return Ok(new { reset = rows.Count, message = "Run POST /run-analysis to reprocess." });
        }

        // ── GET /api/admin/recipe-index/blocked-domains ───────────────────

        /// <summary>
        /// Returns all blocked domains ordered by most-recently blocked, including the reason
        /// and contact email recorded at block time.
        /// </summary>
        [HttpGet("blocked-domains")]
        public async Task<IActionResult> GetBlockedDomains(CancellationToken ct = default)
        {
            var blocked = await _db.BlockedDomains
                .AsNoTracking()
                .OrderByDescending(b => b.BlockedAt)
                .Select(b => new
                {
                    b.Id,
                    b.Domain,
                    b.BlockedAt,
                    b.Reason,
                    b.ContactEmail,
                })
                .ToListAsync(ct);

            return Ok(blocked);
        }

        // ── POST /api/admin/recipe-index/blocked-domains ──────────────────

        /// <summary>
        /// Adds a domain to the blocked list and deactivates its source record if one exists.
        /// Use for manual blocks, robots.txt compliance, or legal requests.
        /// Does not remove existing recipes — call DELETE /domains/{domain}/recipes separately.
        /// </summary>
        [HttpPost("blocked-domains")]
        public async Task<IActionResult> AddBlockedDomain(
            [FromBody] AddBlockedDomainDto dto,
            CancellationToken ct = default)
        {
            var domain = dto.Domain.Trim().ToLowerInvariant();

            var exists = await _db.BlockedDomains.AnyAsync(b => b.Domain == domain, ct);
            if (exists) return Conflict(new { error = $"'{domain}' is already blocked." });

            _db.BlockedDomains.Add(new BlockedDomain
            {
                Domain       = domain,
                Reason       = dto.Reason,
                ContactEmail = dto.ContactEmail,
            });

            // Also deactivate the source if present
            var source = await _db.RecipeIndexSources
                .FirstOrDefaultAsync(s => s.SourceDomain == domain, ct);
            if (source != null) source.IsActive = false;

            await _db.SaveChangesAsync(ct);
            _log.LogInformation("[RecipeIndexAdmin] Domain blocked: {Domain} — {Reason}", domain, dto.Reason);

            return Ok(new { message = $"'{domain}' has been blocked." });
        }

        // ── DELETE /api/admin/recipe-index/blocked-domains/{domain} ──────

        /// <summary>
        /// Removes a domain from the blocked list so the indexer can resume crawling it.
        /// Does not restore previously removed recipes or reactivate the source record.
        /// </summary>
        [HttpDelete("blocked-domains/{domain}")]
        public async Task<IActionResult> RemoveBlockedDomain(
            string domain, CancellationToken ct = default)
        {
            var norm = domain.Trim().ToLowerInvariant();
            var entry = await _db.BlockedDomains
                .FirstOrDefaultAsync(b => b.Domain == norm, ct);

            if (entry == null) return NotFound(new { error = $"'{norm}' is not in the blocked list." });

            _db.BlockedDomains.Remove(entry);
            await _db.SaveChangesAsync(ct);

            _log.LogInformation("[RecipeIndexAdmin] Domain unblocked: {Domain}", norm);
            return Ok(new { message = $"'{norm}' has been unblocked." });
        }

        // ── DELETE /api/admin/recipe-index/domains/{domain}/recipes ──────

        /// <summary>
        /// Removes all indexed recipes belonging to a domain.
        /// Also removes any saved cookbook references to those recipes before deletion
        /// (FK cascade may not cover this path).
        /// Does not block the domain — call POST /blocked-domains separately if needed.
        /// </summary>
        [HttpDelete("domains/{domain}/recipes")]
        public async Task<IActionResult> RemoveDomainRecipes(
            string domain, CancellationToken ct = default)
        {
            var norm = Uri.UnescapeDataString(domain).Trim().ToLowerInvariant();

            var recipes = await _db.RecipeIndex
                .Where(r => r.SourceDomain == norm)
                .ToListAsync(ct);

            if (recipes.Count == 0)
                return NotFound(new { error = $"No recipes found for domain '{norm}'." });

            // Remove saved cookbook references first (FK cascade may not cover this path)
            var ids = recipes.Select(r => r.Id).ToList();
            var saved = await _db.CookbookIndexedRecipes
                .Where(c => ids.Contains(c.RecipeIndexId))
                .ToListAsync(ct);
            _db.CookbookIndexedRecipes.RemoveRange(saved);
            _db.RecipeIndex.RemoveRange(recipes);

            await _db.SaveChangesAsync(ct);
            _log.LogInformation("[RecipeIndexAdmin] Removed {Count} recipes for domain {Domain}", recipes.Count, norm);

            return Ok(new { removed = recipes.Count, domain = norm });
        }

        // ── POST /api/admin/recipe-index/opt-out ─────────────────────────

        /// <summary>
        /// Processes a full domain opt-out in a single atomic operation:
        ///   1. Removes all recipes for the domain (plus their cookbook references).
        ///   2. Adds the domain to the blocked list (skipped if already blocked).
        ///   3. Deactivates the domain's source record so it will not be re-crawled.
        ///
        /// Use this endpoint for DMCA take-down requests or publisher opt-outs that require
        /// both content removal and permanent indexing suppression.
        /// </summary>
        [HttpPost("opt-out")]
        public async Task<IActionResult> OptOut(
            [FromBody] OptOutDto dto,
            CancellationToken ct = default)
        {
            var domain = dto.Domain.Trim().ToLowerInvariant();

            // 1. Remove recipes
            var recipes = await _db.RecipeIndex
                .Where(r => r.SourceDomain == domain)
                .ToListAsync(ct);

            var recipesRemoved = 0;
            if (recipes.Count > 0)
            {
                var ids = recipes.Select(r => r.Id).ToList();
                var saved = await _db.CookbookIndexedRecipes
                    .Where(c => ids.Contains(c.RecipeIndexId))
                    .ToListAsync(ct);
                _db.CookbookIndexedRecipes.RemoveRange(saved);
                _db.RecipeIndex.RemoveRange(recipes);
                recipesRemoved = recipes.Count;
            }

            // 2. Block domain (skip if already blocked)
            var alreadyBlocked = await _db.BlockedDomains
                .AnyAsync(b => b.Domain == domain, ct);
            if (!alreadyBlocked)
            {
                _db.BlockedDomains.Add(new BlockedDomain
                {
                    Domain       = domain,
                    Reason       = dto.Reason ?? "Opt-out request",
                    ContactEmail = dto.ContactEmail,
                });
            }

            // 3. Deactivate source
            var source = await _db.RecipeIndexSources
                .FirstOrDefaultAsync(s => s.SourceDomain == domain, ct);
            if (source != null) source.IsActive = false;

            await _db.SaveChangesAsync(ct);

            _log.LogInformation(
                "[RecipeIndexAdmin] Opt-out processed for {Domain} — {Count} recipes removed, contact: {Email}",
                domain, recipesRemoved, dto.ContactEmail ?? "none");

            return Ok(new
            {
                domain,
                recipesRemoved,
                blocked = true,
                message = $"Opt-out complete for '{domain}'. {recipesRemoved} recipes removed and domain permanently blocked."
            });
        }

        // ── PATCH /api/admin/recipe-index/recipes/{id}/image-blocked ─────

        /// <summary>
        /// Toggles <c>ImageDisplayBlocked</c> on a single RecipeIndex entry.
        /// When true, the recipe's <c>ThumbnailUrl</c> is returned as null in all API responses,
        /// suppressing display without removing the recipe. Use for image-specific opt-outs.
        /// </summary>
        [HttpPatch("recipes/{id:guid}/image-blocked")]
        public async Task<IActionResult> SetImageBlocked(
            Guid id,
            [FromBody] SetImageBlockedDto dto,
            CancellationToken ct = default)
        {
            var recipe = await _db.RecipeIndex.FindAsync(new object[] { id }, ct);
            if (recipe == null) return NotFound();

            recipe.ImageDisplayBlocked = dto.Blocked;
            await _db.SaveChangesAsync(ct);

            return Ok(new { id, imageDisplayBlocked = dto.Blocked });
        }
    }

    // ── Request DTOs ──────────────────────────────────────────────────────────

    public class AddBlockedDomainDto
    {
        [Required, MaxLength(256)]
        public string Domain { get; set; } = string.Empty;

        [MaxLength(1024)]
        public string? Reason { get; set; }

        [MaxLength(256)]
        public string? ContactEmail { get; set; }
    }

    public class OptOutDto
    {
        [Required, MaxLength(256)]
        public string Domain { get; set; } = string.Empty;

        [MaxLength(1024)]
        public string? Reason { get; set; }

        [MaxLength(256)]
        public string? ContactEmail { get; set; }
    }

    public class SetImageBlockedDto
    {
        public bool Blocked { get; set; }
    }
}
