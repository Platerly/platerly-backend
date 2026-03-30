using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;
using System.ComponentModel.DataAnnotations;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// Admin endpoints for managing the Recipe Index Engine.
    ///
    /// GET  /api/admin/recipe-index/stats          — overall + per-domain stats
    /// GET  /api/admin/recipe-index/blocked-domains — list blocked domains
    /// POST /api/admin/recipe-index/blocked-domains — add a domain (opt-out / manual block)
    /// DELETE /api/admin/recipe-index/blocked-domains/{domain} — unblock a domain
    /// DELETE /api/admin/recipe-index/domains/{domain}/recipes  — remove all recipes for a domain
    /// POST /api/admin/recipe-index/opt-out        — full opt-out: remove recipes + block domain
    /// PATCH /api/admin/recipe-index/recipes/{id}/image-blocked — toggle ImageDisplayBlocked
    /// </summary>
    [ApiController]
    [Route("api/admin/recipe-index")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class RecipeIndexAdminController : ControllerBase
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<RecipeIndexAdminController> _log;

        public RecipeIndexAdminController(PlaterlyDbContext db, ILogger<RecipeIndexAdminController> log)
        {
            _db = db;
            _log = log;
        }

        // ── GET /api/admin/recipe-index/stats ─────────────────────────────

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

        // ── GET /api/admin/recipe-index/blocked-domains ───────────────────

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
        // Full opt-out: remove all recipes AND block the domain in one call.

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
