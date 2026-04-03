using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Extensions;
using Platerly.Api.Models;
using Platerly.Api.Services;
using Platerly.Api.Services.Notifications;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2C controller for the Recipzee cookbook feature.
    /// A cookbook is a curated, optionally public collection of recipes (platform-native and
    /// externally indexed). Cookbooks require an active Premium subscription or trial;
    /// free-tier users receive <c>403</c> on all write operations.
    /// Supports: create/update/delete cookbooks, cover image upload, recipe add/remove/reorder/
    /// note, indexed (external) recipe saves with a free-tier cap of 3, collaborative contributors,
    /// and PR-style contribution requests.
    /// Base route: <c>api/cookbook</c>.
    /// Read endpoints are conditionally anonymous (public cookbooks are accessible without auth;
    /// private cookbooks require the caller to be the owner or a contributor).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class CookbookController : ControllerBase
    {
        private readonly PlaterlyDbContext _context;
        private readonly IBlobService _blobService;
        private readonly ILogger<CookbookController> _log;
        private readonly INotificationService _notifications;

        public CookbookController(PlaterlyDbContext context, IBlobService blobService, ILogger<CookbookController> log, INotificationService notifications)
        {
            _context = context;
            _blobService = blobService;
            _log = log;
            _notifications = notifications;
        }

        private Guid? CurrentUserId() => User.GetUserIdOrNull();

        private static IQueryable<T> Page<T>(IQueryable<T> q, int page, int pageSize)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize < 1 ? 20 : (pageSize > 100 ? 100 : pageSize);
            return q.Skip((page - 1) * pageSize).Take(pageSize);
        }

        // ---- Subscription helpers ----

        private async Task<bool> HasActiveCookbookAccessAsync(Guid userId)
        {
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return false;

            var now = DateTime.UtcNow;
            var trialActive = user.TrialEndUtc.HasValue && user.TrialEndUtc > now;
            return user.IsPremium || trialActive;
        }

        /// <summary>
        /// Returns a 403 result with a nice message if the user has no active cookbook access, otherwise null.
        /// </summary>
        private async Task<IActionResult?> ForbidIfNoCookbookAccess(Guid userId)
        {
            var ok = await HasActiveCookbookAccessAsync(userId);
            if (!ok)
            {
                // 403 + JSON body that frontend can display
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = "You don't have an active Recipzee subscription or trial. Upgrade to use cookbooks."
                });
            }

            return null;
        }

        // ---- Permissions helpers ----

        private async Task<bool> CanWriteCookbookAsync(Guid cookbookId, Guid userId)
        {
            // Owner?
            var isOwner = await _context.Cookbooks.AnyAsync(c => c.Id == cookbookId && c.UserId == userId);
            if (isOwner) return true;

            // Contributor with "Write"?
            return await _context.CookbookContributors
                .AnyAsync(cc => cc.CookbookId == cookbookId && cc.UserId == userId && cc.Role == "Write");
        }

        private async Task<bool> CanReadCookbookAsync(Cookbook cb, Guid? userId)
        {
            if (cb.IsPublic) return true;
            if (userId is null) return false;
            if (cb.UserId == userId.Value) return true;

            return await _context.CookbookContributors
                .AnyAsync(cc => cc.CookbookId == cb.Id && cc.UserId == userId.Value);
        }

        // ---------------------------
        // Create / Update cookbook
        // ---------------------------

        /// <summary>
        /// Create a new cookbook for the authenticated user.
        /// Requires an active subscription or trial; returns <c>403</c> otherwise.
        /// </summary>
        /// <param name="dto">Title (required), optional description, and visibility flag.</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ id: guid }</c> of the created cookbook.<br/>
        /// <c>400</c> — title missing.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>403</c> — no active subscription or trial.
        /// </returns>
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> CreateCookbook([FromBody] CreateCookbookDto dto)
        {
            var uid = CurrentUserId();
            if (uid is null) return Unauthorized();

            if (string.IsNullOrWhiteSpace(dto.Title))
                return BadRequest("Title is required.");

            // 🔐 SUBSCRIPTION CHECK
            var forbid = await ForbidIfNoCookbookAccess(uid.Value);
            if (forbid != null) return forbid;

            var cookbook = new Cookbook
            {
                Title = dto.Title.Trim(),
                Description = dto.Description?.Trim(),
                IsPublic = dto.IsPublic,
                UserId = uid.Value,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Cookbooks.Add(cookbook);
            await _context.SaveChangesAsync();

            return Ok(new { cookbook.Id });
        }

        /// <summary>
        /// Update a cookbook's title, description, or public/private visibility.
        /// Only the cookbook owner may change metadata (contributors cannot).
        /// Requires an active subscription or trial.
        /// </summary>
        /// <param name="cookbookId">The cookbook GUID to update.</param>
        /// <param name="dto">Fields to update (null values are ignored).</param>
        /// <returns>
        /// <c>204 No Content</c> on success.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>403</c> — no subscription/trial, or caller is not the owner.<br/>
        /// <c>404</c> — cookbook not found.
        /// </returns>
        [Authorize]
        [HttpPut("{cookbookId:guid}")]
        public async Task<IActionResult> UpdateCookbook(Guid cookbookId, [FromBody] UpdateCookbookDto dto)
        {
            var uid = CurrentUserId();
            if (uid is null) return Unauthorized();

            // 🔐 SUBSCRIPTION CHECK
            var forbid = await ForbidIfNoCookbookAccess(uid.Value);
            if (forbid != null) return forbid;

            var cb = await _context.Cookbooks.FirstOrDefaultAsync(c => c.Id == cookbookId);
            if (cb == null) return NotFound("Cookbook not found.");
            if (cb.UserId != uid.Value) return Forbid(); // owner-only for meta

            if (!string.IsNullOrWhiteSpace(dto.Title)) cb.Title = dto.Title.Trim();
            if (dto.Description != null) cb.Description = dto.Description.Trim();
            if (dto.IsPublic.HasValue) cb.IsPublic = dto.IsPublic.Value;
            cb.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // ---------------------------
        // Cover image upload
        // ---------------------------

        /// <summary>
        /// Upload or replace the cookbook's cover image. Owner-only. Follows safe-swap:
        /// new blob is uploaded first, then the DB is updated, then the old blob is deleted
        /// best-effort. Requires subscription/trial.
        /// </summary>
        /// <param name="cookbookId">The cookbook GUID.</param>
        /// <param name="input">Multipart form with the cover image file.</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ coverUrl, coverImageUrl }</c> (both fields for client compatibility).<br/>
        /// <c>400</c> — no file uploaded.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>403</c> — no subscription/trial or not the owner.<br/>
        /// <c>404</c> — cookbook not found.<br/>
        /// <c>500</c> — blob upload failure.
        /// </returns>
        [Authorize]
        [HttpPut("{cookbookId:guid}/cover")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadCover(Guid cookbookId, [FromForm] FileUploadDto input)
        {
            var uid = CurrentUserId();
            if (uid is null) return Unauthorized();

            // 🔐 SUBSCRIPTION CHECK
            var forbid = await ForbidIfNoCookbookAccess(uid.Value);
            if (forbid != null) return forbid;

            var cb = await _context.Cookbooks.FirstOrDefaultAsync(c => c.Id == cookbookId);
            if (cb == null) return NotFound("Cookbook not found.");
            if (cb.UserId != uid.Value) return Forbid(); // owner-only

            if (input?.File == null || input.File.Length == 0)
                return BadRequest("No file uploaded.");

            var newBlobName = $"cookbook_{cookbookId}_{Guid.NewGuid()}{Path.GetExtension(input.File.FileName)}";
            string newUrl;
            try
            {
                newUrl = await _blobService.UploadCookbookCoverAsync(input.File, newBlobName);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to upload cookbook cover for {CookbookId}", cookbookId);
                return StatusCode(StatusCodes.Status500InternalServerError, "Upload failed.");
            }

            var oldBlobName = cb.CoverImageBlobName;
            cb.CoverImageUrl = newUrl;
            cb.CoverImageBlobName = newBlobName;
            cb.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // best-effort delete old
            try
            {
                if (!string.IsNullOrWhiteSpace(oldBlobName) && oldBlobName != newBlobName)
                    _ = _blobService.DeleteCookbookCoverIfExistsAsync(oldBlobName);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to delete old cookbook cover {Blob}", oldBlobName);
            }

            // Return both casings so any client code path works
            return Ok(new { coverUrl = newUrl, coverImageUrl = newUrl });
        }

        /// <summary>
        /// Permanently delete a cookbook and cascade-remove all recipe associations and contributors.
        /// Owner-only. Cover image blob is deleted best-effort. Requires subscription/trial.
        /// </summary>
        /// <param name="cookbookId">The cookbook GUID to delete.</param>
        /// <returns>
        /// <c>204 No Content</c> on success.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>403</c> — no subscription/trial or not the owner.<br/>
        /// <c>404</c> — cookbook not found.
        /// </returns>
        [Authorize]
        [HttpDelete("{cookbookId:guid}")]
        public async Task<IActionResult> DeleteCookbook(Guid cookbookId)
        {
            var uid = CurrentUserId();
            if (uid is null) return Unauthorized();

            // 🔐 SUBSCRIPTION CHECK
            var forbid = await ForbidIfNoCookbookAccess(uid.Value);
            if (forbid != null) return forbid;

            var cb = await _context.Cookbooks.FirstOrDefaultAsync(c => c.Id == cookbookId);
            if (cb == null) return NotFound("Cookbook not found.");
            if (cb.UserId != uid.Value) return Forbid(); // owner-only

            var oldBlob = cb.CoverImageBlobName;

            _context.Cookbooks.Remove(cb); // CookbookRecipes & Contributors cascade if configured
            await _context.SaveChangesAsync();

            // best-effort cover cleanup
            try { if (!string.IsNullOrWhiteSpace(oldBlob)) _ = _blobService.DeleteCookbookCoverIfExistsAsync(oldBlob); }
            catch (Exception ex) { _log.LogWarning(ex, "Failed to delete cover {Blob}", oldBlob); }

            return NoContent();
        }

        // ---------------------------
        // Add / remove recipe
        // ---------------------------

        /// <summary>
        /// Add a platform-native recipe to the cookbook. Requires write access (owner or
        /// contributor with "Write" role) and an active subscription. If <c>SortOrder</c>
        /// is omitted, the recipe is appended after the current last entry.
        /// </summary>
        /// <param name="cookbookId">The target cookbook GUID.</param>
        /// <param name="dto">RecipeId, optional SortOrder, and optional Note.</param>
        /// <returns>
        /// <c>200 OK</c> on success.<br/>
        /// <c>400</c> — recipe already in this cookbook.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>403</c> — no subscription/trial or insufficient write access.<br/>
        /// <c>404</c> — cookbook or recipe not found.
        /// </returns>
        [Authorize]
        [HttpPost("{cookbookId:guid}/recipes")]
        public async Task<IActionResult> AddRecipe(Guid cookbookId, [FromBody] AddRecipeToCookbookDto dto)
        {
            var uid = CurrentUserId();
            if (uid is null) return Unauthorized();

            // 🔐 SUBSCRIPTION CHECK
            var forbid = await ForbidIfNoCookbookAccess(uid.Value);
            if (forbid != null) return forbid;

            var cb = await _context.Cookbooks.FirstOrDefaultAsync(c => c.Id == cookbookId);
            if (cb == null) return NotFound("Cookbook not found.");

            if (!await CanWriteCookbookAsync(cookbookId, uid.Value)) return Forbid();

            // Use AnyAsync for existence check — avoids loading the full Recipe entity graph.
            if (!await _context.Recipes.AnyAsync(r => r.Id == dto.RecipeId))
                return NotFound("Recipe not found.");

            var exists = await _context.CookbookRecipes.AnyAsync(cr => cr.CookbookId == cookbookId && cr.RecipeId == dto.RecipeId);
            if (exists) return BadRequest("Recipe already in this cookbook.");

            // Determine next SortOrder if none supplied
            int sortOrder;
            if (dto.SortOrder.HasValue)
            {
                sortOrder = dto.SortOrder.Value;
            }
            else
            {
                var max = await _context.CookbookRecipes
                    .Where(cr => cr.CookbookId == cookbookId)
                    .Select(cr => (int?)cr.SortOrder)
                    .MaxAsync();
                sortOrder = (max ?? -1) + 1;
            }

            var crNew = new CookbookRecipe
            {
                CookbookId = cookbookId,
                RecipeId = dto.RecipeId,
                SortOrder = sortOrder,
                Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
                AddedByUserId = uid.Value,
                AddedAt = DateTime.UtcNow
            };

            _context.CookbookRecipes.Add(crNew);
            await _context.SaveChangesAsync();

            return Ok();
        }

        /// <summary>
        /// Remove a platform-native recipe from the cookbook. Requires write access (owner or
        /// contributor with "Write" role) and an active subscription.
        /// </summary>
        /// <param name="cookbookId">The cookbook GUID.</param>
        /// <param name="recipeId">The recipe GUID to remove.</param>
        /// <returns>
        /// <c>204 No Content</c> on success.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>403</c> — no subscription/trial or insufficient write access.<br/>
        /// <c>404</c> — cookbook not found, or recipe not in this cookbook.
        /// </returns>
        [Authorize]
        [HttpDelete("{cookbookId:guid}/recipes/{recipeId:guid}")]
        public async Task<IActionResult> RemoveRecipe(Guid cookbookId, Guid recipeId)
        {
            var uid = CurrentUserId();
            if (uid is null) return Unauthorized();

            // 🔐 SUBSCRIPTION CHECK
            var forbid = await ForbidIfNoCookbookAccess(uid.Value);
            if (forbid != null) return forbid;

            var cb = await _context.Cookbooks.FirstOrDefaultAsync(c => c.Id == cookbookId);
            if (cb == null) return NotFound("Cookbook not found.");

            if (!await CanWriteCookbookAsync(cookbookId, uid.Value)) return Forbid();

            var cr = await _context.CookbookRecipes.FirstOrDefaultAsync(e => e.CookbookId == cookbookId && e.RecipeId == recipeId);
            if (cr == null) return NotFound("Recipe not found in this cookbook.");

            _context.CookbookRecipes.Remove(cr);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // ---------------------------
        // Reorder & note update
        // ---------------------------

        /// <summary>
        /// Reorder recipes within a cookbook by assigning a new <c>SortOrder</c> to each recipe.
        /// After applying the caller-supplied ordering the positions are normalised to a contiguous
        /// 0..N sequence so there are no duplicates or gaps. Requires write access and an active
        /// subscription.
        /// </summary>
        /// <param name="cookbookId">The cookbook GUID.</param>
        /// <param name="dto">List of <c>{ recipeId, sortOrder }</c> pairs.</param>
        /// <returns>
        /// <c>204 No Content</c> on success.<br/>
        /// <c>400</c> — empty ordering list.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>403</c> — no subscription/trial or insufficient write access.<br/>
        /// <c>404</c> — cookbook not found.
        /// </returns>
        [Authorize]
        [HttpPut("{cookbookId:guid}/recipes/order")]
        public async Task<IActionResult> Reorder(Guid cookbookId, [FromBody] ReorderCookbookDto dto)
        {
            var uid = CurrentUserId();
            if (uid is null) return Unauthorized();

            // 🔐 SUBSCRIPTION CHECK
            var forbid = await ForbidIfNoCookbookAccess(uid.Value);
            if (forbid != null) return forbid;

            var cb = await _context.Cookbooks.FirstOrDefaultAsync(c => c.Id == cookbookId);
            if (cb == null) return NotFound("Cookbook not found.");

            if (!await CanWriteCookbookAsync(cookbookId, uid.Value)) return Forbid();

            var map = dto.Items?.ToDictionary(x => x.RecipeId, x => x.SortOrder) ?? new Dictionary<Guid, int>();
            if (map.Count == 0) return BadRequest("No ordering provided.");

            var entries = await _context.CookbookRecipes.Where(cr => cr.CookbookId == cookbookId).ToListAsync();

            foreach (var e in entries)
            {
                if (map.TryGetValue(e.RecipeId, out var so))
                    e.SortOrder = so;
            }

            // Normalize to 0..N to avoid duplicates/gaps
            var ordered = entries
                .OrderBy(e => e.SortOrder)
                .ThenBy(e => e.AddedAt)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
                ordered[i].SortOrder = i;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>
        /// Add or update the personal note attached to a specific recipe inside the cookbook.
        /// Passing a blank or null <c>Note</c> clears the note. Requires write access and an
        /// active subscription.
        /// </summary>
        /// <param name="cookbookId">The cookbook GUID.</param>
        /// <param name="recipeId">The recipe GUID whose note should be updated.</param>
        /// <param name="dto">Object containing the new <c>Note</c> text (nullable).</param>
        /// <returns>
        /// <c>204 No Content</c> on success.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>403</c> — no subscription/trial or insufficient write access.<br/>
        /// <c>404</c> — cookbook not found, or recipe not in this cookbook.
        /// </returns>
        [Authorize]
        [HttpPut("{cookbookId:guid}/recipes/{recipeId:guid}/note")]
        public async Task<IActionResult> UpdateNote(Guid cookbookId, Guid recipeId, [FromBody] UpdateCookbookNoteDto dto)
        {
            var uid = CurrentUserId();
            if (uid is null) return Unauthorized();

            // 🔐 SUBSCRIPTION CHECK
            var forbid = await ForbidIfNoCookbookAccess(uid.Value);
            if (forbid != null) return forbid;

            var cb = await _context.Cookbooks.FirstOrDefaultAsync(c => c.Id == cookbookId);
            if (cb == null) return NotFound("Cookbook not found.");

            if (!await CanWriteCookbookAsync(cookbookId, uid.Value)) return Forbid();

            var cr = await _context.CookbookRecipes.FirstOrDefaultAsync(e => e.CookbookId == cookbookId && e.RecipeId == recipeId);
            if (cr == null) return NotFound("Recipe not found in this cookbook.");

            cr.Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // ---------------------------
        // Read endpoints
        // ---------------------------

        /// <summary>
        /// List all cookbooks owned by the authenticated user, ordered by most recently updated.
        /// Returns a paged result including recipe count per cookbook. Does not require a
        /// subscription (read is always permitted).
        /// </summary>
        /// <param name="page">1-based page number (default 1).</param>
        /// <param name="pageSize">Items per page, clamped to 1–100 (default 20).</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ page, pageSize, total, items: CookbookSummaryDto[] }</c>.<br/>
        /// <c>401</c> — unauthenticated.
        /// </returns>
        [Authorize]
        [HttpGet("mine")]
        public async Task<IActionResult> GetMyCookbooks([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var uid = CurrentUserId();
            if (uid is null) return Unauthorized();

            var q = _context.Cookbooks
                .AsNoTracking()
                .Where(c => c.UserId == uid.Value)
                .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
                .Select(c => new CookbookSummaryDto
                {
                    Id = c.Id,
                    Title = c.Title,
                    Description = c.Description,
                    IsPublic = c.IsPublic,
                    CoverImageUrl = c.CoverImageUrl,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt,
                    RecipeCount = _context.CookbookRecipes.Count(cr => cr.CookbookId == c.Id)
                });

            var total = await q.CountAsync();
            var items = await Page(q, page, pageSize).ToListAsync();

            return Ok(new { page, pageSize, total, items });
        }

        /// <summary>
        /// List all public cookbooks across the platform. Anonymous callers are permitted.
        /// Supports an optional full-text search across cookbook title, description, and owner
        /// username. Ordered by most recently updated.
        /// </summary>
        /// <param name="page">1-based page number (default 1).</param>
        /// <param name="pageSize">Items per page, clamped to 1–100 (default 20).</param>
        /// <param name="search">Optional search term (case-insensitive partial match).</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ page, pageSize, total, totalCount, totalPages, items: CookbookSummaryDto[] }</c>.
        /// </returns>
        [AllowAnonymous]
        [HttpGet("public")]
        public async Task<IActionResult> GetPublicCookbooks([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null)
        {
            var q = _context.Cookbooks
                .AsNoTracking()
                .Where(c => c.IsPublic);

            // Apply search filter if provided
            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchLower = search.Trim().ToLower();
                q = q.Where(c =>
                    c.Title.ToLower().Contains(searchLower) ||
                    (c.Description != null && c.Description.ToLower().Contains(searchLower)) ||
                    (c.User != null && c.User.Username != null && c.User.Username.ToLower().Contains(searchLower)));
            }

            var projectedQ = q
                .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
                .Select(c => new CookbookSummaryDto
                {
                    Id = c.Id,
                    Title = c.Title,
                    Description = c.Description,
                    IsPublic = c.IsPublic,
                    CoverImageUrl = c.CoverImageUrl,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt,
                    RecipeCount = _context.CookbookRecipes.Count(cr => cr.CookbookId == c.Id),
                    OwnerUsername = c.User != null ? c.User.Username : null
                });

            var total = await projectedQ.CountAsync();
            var items = await Page(projectedQ, page, pageSize).ToListAsync();

            return Ok(new { page, pageSize, total, totalCount = total, totalPages = (int)Math.Ceiling((double)total / pageSize), items });
        }

        /// <summary>
        /// List all public cookbooks belonging to a specific user. Anonymous callers are permitted.
        /// Used on public profile pages to display another user's shared cookbook collection.
        /// </summary>
        /// <param name="userId">The target user's GUID.</param>
        /// <param name="page">1-based page number (default 1).</param>
        /// <param name="pageSize">Items per page, clamped to 1–100 (default 20).</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ page, pageSize, total, items: CookbookSummaryDto[] }</c>.
        /// </returns>
        [AllowAnonymous]
        [HttpGet("user/{userId:guid}/public")]
        public async Task<IActionResult> GetUserPublicCookbooks(Guid userId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var q = _context.Cookbooks
                .AsNoTracking()
                .Where(c => c.UserId == userId && c.IsPublic)
                .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
                .Select(c => new CookbookSummaryDto
                {
                    Id = c.Id,
                    Title = c.Title,
                    Description = c.Description,
                    IsPublic = c.IsPublic,
                    CoverImageUrl = c.CoverImageUrl,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt,
                    RecipeCount = _context.CookbookRecipes.Count(cr => cr.CookbookId == c.Id)
                });

            var total = await q.CountAsync();
            var items = await Page(q, page, pageSize).ToListAsync();

            return Ok(new { page, pageSize, total, items });
        }

        /// <summary>
        /// Retrieve full cookbook details including all recipes (platform-native and indexed),
        /// ordered by <c>SortOrder</c>. Access rules: public cookbooks are visible to everyone;
        /// private cookbooks require the caller to be the owner or a contributor. Unauthorised
        /// callers on a private cookbook receive <c>404</c> to avoid enumeration.
        /// </summary>
        /// <param name="cookbookId">The cookbook GUID to retrieve.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="CookbookDetailsDto"/> with <c>Items</c> (native recipes)
        /// and <c>IndexedItems</c> (external recipes including allergen snapshot JSON).<br/>
        /// <c>404</c> — not found or access denied (private, caller not authorised).
        /// </returns>
        [AllowAnonymous]
        [HttpGet("{cookbookId:guid}")]
        public async Task<IActionResult> GetCookbook(Guid cookbookId)
        {
            var uid = CurrentUserId();

            var cb = await _context.Cookbooks.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cookbookId);
            if (cb == null) return NotFound();

            if (!await CanReadCookbookAsync(cb, uid))
                return NotFound();

            var items = await _context.CookbookRecipes
                .AsNoTracking()
                .Where(cr => cr.CookbookId == cookbookId)
                .OrderBy(cr => cr.SortOrder)
                .ThenBy(cr => cr.AddedAt)
                .Select(cr => new CookbookRecipeItemDto
                {
                    RecipeId = cr.RecipeId,
                    Title = cr.Recipe.Title,
                    Description = cr.Recipe.Description,
                    ImageUrl = cr.Recipe.ImageUrl,
                    SortOrder = cr.SortOrder,
                    Note = cr.Note,
                    AddedByUserId = cr.AddedByUserId,
                    AddedByUsername = _context.Users
                        .Where(u => u.Id == cr.AddedByUserId)
                        .Select(u => u.Username)
                        .FirstOrDefault(),
                    AddedAt = cr.AddedAt
                })
                .ToListAsync();

            // Load indexed (externally-sourced) recipes saved to this cookbook
            var indexedItems = await _context.CookbookIndexedRecipes
                .AsNoTracking()
                .Where(ci => ci.CookbookId == cookbookId)
                .OrderBy(ci => ci.SavedAt)
                .Select(ci => new CookbookIndexedRecipeItemDto
                {
                    RecipeIndexId        = ci.RecipeIndexId,
                    Title                = ci.RecipeIndex.Title,
                    ThumbnailUrl         = ci.RecipeIndex.ThumbnailUrl,
                    SourceDomain         = ci.RecipeIndex.SourceDomain,
                    SourceUrl            = ci.RecipeIndex.SourceUrl,
                    CuisineType          = ci.RecipeIndex.CuisineType,
                    NickelRiskLevel      = "Unknown", // resolved client-side from AllergenSnapshotJson
                    AllergenSnapshotJson = ci.AllergenSnapshotJson,
                    Note                 = ci.Note,
                    SavedAt              = ci.SavedAt
                })
                .ToListAsync();

            var result = new CookbookDetailsDto
            {
                Id = cb.Id,
                Title = cb.Title,
                Description = cb.Description,
                IsPublic = cb.IsPublic,
                CoverImageUrl = cb.CoverImageUrl,
                CreatedAt = cb.CreatedAt,
                UpdatedAt = cb.UpdatedAt,
                Items = items,
                IndexedItems = indexedItems,
                RecipeCount = items.Count + indexedItems.Count
            };

            return Ok(result);
        }

        // ---------------------------
        // Indexed recipe saves
        // ---------------------------

        private const int FreeIndexedRecipeSaveLimit = 3;

        /// <summary>
        /// Save an externally-indexed (web-crawled) recipe to the cookbook. Free-tier users are
        /// limited to <c>3</c> indexed saves across all their cookbooks; exceeding the limit
        /// returns <c>402</c> with current usage. Premium/trial users have no cap.
        /// The allergen analysis snapshot from the index is persisted alongside the save so it
        /// is available offline without re-fetching.
        /// </summary>
        /// <param name="cookbookId">The target cookbook GUID.</param>
        /// <param name="dto">RecipeIndexId and optional note.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ id: guid }</c> of the new save record.<br/>
        /// <c>400</c> — recipe already saved to this cookbook.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>402</c> — free-tier save limit reached.<br/>
        /// <c>403</c> — insufficient write access to the cookbook.<br/>
        /// <c>404</c> — cookbook or indexed recipe not found.
        /// </returns>
        [Authorize]
        [HttpPost("{cookbookId:guid}/indexed-recipes")]
        public async Task<IActionResult> AddIndexedRecipe(
            Guid cookbookId,
            [FromBody] AddIndexedRecipeToCookbookDto dto,
            CancellationToken ct = default)
        {
            var uid = CurrentUserId();
            if (uid is null) return Unauthorized();

            var cb = await _context.Cookbooks.FirstOrDefaultAsync(c => c.Id == cookbookId, ct);
            if (cb == null) return NotFound("Cookbook not found.");

            if (!await CanWriteCookbookAsync(cookbookId, uid.Value)) return Forbid();

            // Free tier gate: max 3 indexed recipe saves per user across all cookbooks
            var user = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == uid.Value, ct);

            bool isPremiumOrTrial = (user?.IsPremium ?? false) ||
                (user?.TrialEndUtc.HasValue == true && user.TrialEndUtc.Value > DateTimeOffset.UtcNow);

            if (!isPremiumOrTrial)
            {
                var totalSaved = await _context.CookbookIndexedRecipes
                    .CountAsync(ci => ci.SavedByUserId == uid.Value, ct);

                if (totalSaved >= FreeIndexedRecipeSaveLimit)
                    return StatusCode(402, new
                    {
                        message = $"Free accounts can save up to {FreeIndexedRecipeSaveLimit} indexed recipes. " +
                                  "Upgrade to Premium for unlimited cookbook saves.",
                        limit = FreeIndexedRecipeSaveLimit,
                        current = totalSaved
                    });
            }

            // Validate the RecipeIndex record exists
            var indexed = await _context.RecipeIndex
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == dto.RecipeIndexId, ct);

            if (indexed == null) return NotFound("Indexed recipe not found.");

            // Idempotent: already saved to this cookbook?
            var alreadyExists = await _context.CookbookIndexedRecipes
                .AnyAsync(ci => ci.CookbookId == cookbookId && ci.RecipeIndexId == dto.RecipeIndexId, ct);

            if (alreadyExists) return BadRequest("Recipe already saved to this cookbook.");

            var entry = new CookbookIndexedRecipe
            {
                CookbookId           = cookbookId,
                RecipeIndexId        = dto.RecipeIndexId,
                SavedByUserId        = uid.Value,
                AllergenSnapshotJson = indexed.AllergenAnalysisJson,
                SavedAt              = DateTime.UtcNow
            };

            _context.CookbookIndexedRecipes.Add(entry);
            await _context.SaveChangesAsync(ct);

            return Ok(new { id = entry.Id });
        }

        /// <summary>
        /// Remove a previously saved indexed recipe from the cookbook. Requires write access
        /// (owner or "Write" contributor). Does not decrement the user's free-tier save counter
        /// (the count is recalculated live from the DB).
        /// </summary>
        /// <param name="cookbookId">The cookbook GUID.</param>
        /// <param name="recipeIndexId">The indexed recipe GUID to remove.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>204 No Content</c> on success.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>403</c> — insufficient write access.<br/>
        /// <c>404</c> — save record not found.
        /// </returns>
        [Authorize]
        [HttpDelete("{cookbookId:guid}/indexed-recipes/{recipeIndexId:guid}")]
        public async Task<IActionResult> RemoveIndexedRecipe(
            Guid cookbookId, Guid recipeIndexId, CancellationToken ct = default)
        {
            var uid = CurrentUserId();
            if (uid is null) return Unauthorized();

            if (!await CanWriteCookbookAsync(cookbookId, uid.Value)) return Forbid();

            var entry = await _context.CookbookIndexedRecipes
                .FirstOrDefaultAsync(ci => ci.CookbookId == cookbookId && ci.RecipeIndexId == recipeIndexId, ct);

            if (entry == null) return NotFound();

            _context.CookbookIndexedRecipes.Remove(entry);
            await _context.SaveChangesAsync(ct);
            return NoContent();
        }

        // ---------------------------
        // Contributors (owner-managed)
        // ---------------------------

        /// <summary>
        /// Add a user as a cookbook contributor. Owner-only. Both the owner and the invitee must
        /// have an active subscription or trial. The invitee is notified via
        /// <see cref="INotificationService.NotifyCookbookInviteAsync"/>. Role defaults to
        /// <c>"Write"</c> if not specified; accepted values are <c>"Read"</c> and <c>"Write"</c>.
        /// </summary>
        /// <param name="cookbookId">The cookbook GUID.</param>
        /// <param name="dto">UserId of the invitee and optional Role.</param>
        /// <returns>
        /// <c>204 No Content</c> on success.<br/>
        /// <c>400</c> — invitee is the owner, already a contributor, or lacks a subscription.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>403</c> — caller has no subscription/trial, or is not the owner.<br/>
        /// <c>404</c> — cookbook not found.
        /// </returns>
        [Authorize]
        [HttpPost("{cookbookId:guid}/contributors")]
        public async Task<IActionResult> AddContributor(Guid cookbookId, [FromBody] AddContributorDto dto)
        {
            var uid = CurrentUserId();
            if (uid is null) return Unauthorized();

            // Owner must have subscription to manage contributors
            var forbid = await ForbidIfNoCookbookAccess(uid.Value);
            if (forbid != null) return forbid;

            var cb = await _context.Cookbooks
                .Include(c => c.Contributors)
                .FirstOrDefaultAsync(c => c.Id == cookbookId);
            if (cb == null) return NotFound("Cookbook not found.");
            if (cb.UserId != uid.Value) return Forbid();

            if (dto.UserId == cb.UserId) return BadRequest("Owner already has full permissions.");
            if (cb.Contributors.Any(c => c.UserId == dto.UserId)) return BadRequest("User is already a contributor.");

            // 🔐 SUBSCRIPTION CHECK FOR TARGET USER
            var targetUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == dto.UserId);
            if (targetUser == null) return BadRequest("User not found.");

            var now = DateTime.UtcNow;
            var targetTrialActive = targetUser.TrialEndUtc.HasValue && targetUser.TrialEndUtc > now;
            var targetCanUseCookbooks = targetUser.IsPremium || targetTrialActive;

            if (!targetCanUseCookbooks)
                return BadRequest("This user must have an active subscription or trial to be a cookbook contributor.");

            _context.CookbookContributors.Add(new CookbookContributor
            {
                CookbookId = cookbookId,
                UserId = dto.UserId,
                Role = string.IsNullOrWhiteSpace(dto.Role) ? "Write" : dto.Role.Trim()
            });

            await _context.SaveChangesAsync();

            // Notify the invited user
            await _notifications.NotifyCookbookInviteAsync(dto.UserId, uid.Value, cookbookId);

            return NoContent();
        }

        /// <summary>
        /// Change the role of an existing contributor. Owner-only. Accepted roles are
        /// <c>"Read"</c> (view-only) and <c>"Write"</c> (view + add/remove recipes, reorder, notes).
        /// </summary>
        /// <param name="cookbookId">The cookbook GUID.</param>
        /// <param name="userId">The contributor's user GUID.</param>
        /// <param name="dto">New role string.</param>
        /// <returns>
        /// <c>204 No Content</c> on success.<br/>
        /// <c>400</c> — invalid role value.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>403</c> — caller is not the cookbook owner.<br/>
        /// <c>404</c> — cookbook or contributor not found.
        /// </returns>
        [Authorize]
        [HttpPut("{cookbookId:guid}/contributors/{userId:guid}/role")]
        public async Task<IActionResult> UpdateContributorRole(Guid cookbookId, Guid userId, [FromBody] UpdateContributorRoleDto dto)
        {
            var uid = CurrentUserId();
            if (uid is null) return Unauthorized();

            var cb = await _context.Cookbooks.FirstOrDefaultAsync(c => c.Id == cookbookId);
            if (cb == null) return NotFound("Cookbook not found.");

            // Only owner can update roles
            if (cb.UserId != uid.Value) return Forbid();

            var contributor = await _context.CookbookContributors
                .FirstOrDefaultAsync(cc => cc.CookbookId == cookbookId && cc.UserId == userId);
            if (contributor == null) return NotFound("Contributor not found.");

            // Validate role
            var validRoles = new[] { "Read", "Write" };
            var newRole = dto.Role?.Trim() ?? "Read";
            if (!validRoles.Contains(newRole))
                return BadRequest("Invalid role. Use 'Read' or 'Write'.");

            contributor.Role = newRole;
            await _context.SaveChangesAsync();

            return NoContent();
        }

        /// <summary>
        /// List all contributors for a cookbook, ordered by when they were added. Owner-only.
        /// Returns each contributor's UserId, Role, DisplayName, and AddedAt timestamp.
        /// </summary>
        /// <param name="cookbookId">The cookbook GUID.</param>
        /// <returns>
        /// <c>200 OK</c> — array of <see cref="ContributorDto"/>.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>403</c> — caller is not the cookbook owner.<br/>
        /// <c>404</c> — cookbook not found.
        /// </returns>
        [Authorize]
        [HttpGet("{cookbookId:guid}/contributors")]
        public async Task<IActionResult> ListContributors(Guid cookbookId)
        {
            var uid = CurrentUserId();
            if (uid is null) return Unauthorized();

            var cb = await _context.Cookbooks
                .Include(c => c.Contributors).ThenInclude(cc => cc.User)
                .FirstOrDefaultAsync(c => c.Id == cookbookId);
            if (cb == null) return NotFound("Cookbook not found.");
            if (cb.UserId != uid.Value) return Forbid();

            var list = cb.Contributors
                .OrderBy(c => c.AddedAt)
                .Select(c => new ContributorDto
                {
                    UserId = c.UserId,
                    Role = c.Role,
                    DisplayName = c.User.DisplayName,
                    AddedAt = c.AddedAt
                })
                .ToList();

            return Ok(list);
        }

        /// <summary>
        /// Remove a contributor from the cookbook, revoking their access immediately. Owner-only.
        /// </summary>
        /// <param name="cookbookId">The cookbook GUID.</param>
        /// <param name="userId">The contributor's user GUID to remove.</param>
        /// <returns>
        /// <c>204 No Content</c> on success.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>403</c> — caller is not the cookbook owner.<br/>
        /// <c>404</c> — cookbook or contributor not found.
        /// </returns>
        [Authorize]
        [HttpDelete("{cookbookId:guid}/contributors/{userId:guid}")]
        public async Task<IActionResult> RemoveContributor(Guid cookbookId, Guid userId)
        {
            var uid = CurrentUserId();
            if (uid is null) return Unauthorized();

            var cb = await _context.Cookbooks
                .Include(c => c.Contributors)
                .FirstOrDefaultAsync(c => c.Id == cookbookId);
            if (cb == null) return NotFound("Cookbook not found.");
            if (cb.UserId != uid.Value) return Forbid();

            var cc = cb.Contributors.FirstOrDefault(x => x.UserId == userId);
            if (cc == null) return NotFound("Contributor not found.");

            _context.CookbookContributors.Remove(cc);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>
        /// List all cookbooks the authenticated user can write to — i.e. cookbooks they own plus
        /// cookbooks where they hold a <c>"Write"</c> contributor role. Used by the recipe-add
        /// flow to let contributors select a destination cookbook.
        /// </summary>
        /// <param name="page">1-based page number (default 1).</param>
        /// <param name="pageSize">Items per page, clamped to 1–100 (default 20).</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ page, pageSize, total, items: CookbookSummaryDto[] }</c>.<br/>
        /// <c>401</c> — unauthenticated.
        /// </returns>
        [Authorize]
        [HttpGet("can-write")]
        public async Task<IActionResult> GetCookbooksICanWrite([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var uid = CurrentUserId();
            if (uid is null) return Unauthorized();

            var q = _context.Cookbooks
                .AsNoTracking()
                .Where(c => c.UserId == uid.Value
                    || _context.CookbookContributors.Any(cc =>
                           cc.CookbookId == c.Id &&
                           cc.UserId == uid.Value &&
                           cc.Role == "Write"))
                .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
                .Select(c => new CookbookSummaryDto
                {
                    Id = c.Id,
                    Title = c.Title,
                    Description = c.Description,
                    IsPublic = c.IsPublic,
                    CoverImageUrl = c.CoverImageUrl,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt,
                    RecipeCount = _context.CookbookRecipes.Count(cr => cr.CookbookId == c.Id)
                });

            var total = await q.CountAsync();
            var items = await Page(q, page, pageSize).ToListAsync();
            return Ok(new { page, pageSize, total, items });
        }

        // ---------------------------
        // Contribution Requests (PR-like)
        // ---------------------------

        /// <summary>
        /// Submit a PR-style request to add a specific recipe to a public cookbook. Any
        /// authenticated user may request; the cookbook owner must approve before the recipe
        /// is actually added. Duplicate pending requests for the same recipe by the same user are
        /// rejected. The cookbook owner is notified via
        /// <see cref="INotificationService.NotifyCookbookRecipeRequestAsync"/>.
        /// </summary>
        /// <param name="cookbookId">The target cookbook GUID.</param>
        /// <param name="dto">RecipeId to propose and an optional message to the owner.</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ id: guid }</c> of the new contribution request.<br/>
        /// <c>400</c> — recipe already in cookbook, duplicate pending request, or cookbook is private
        ///   and caller is not the owner.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — cookbook or recipe not found.
        /// </returns>
        [Authorize]
        [HttpPost("{cookbookId:guid}/requests")]
        public async Task<IActionResult> CreateContributionRequest(Guid cookbookId, [FromBody] CreateContributionRequestDto dto)
        {
            var requesterId = CurrentUserId();
            if (requesterId is null) return Unauthorized();

            var cb = await _context.Cookbooks.FirstOrDefaultAsync(c => c.Id == cookbookId);
            if (cb == null) return NotFound("Cookbook not found.");

            if (!cb.IsPublic && cb.UserId != requesterId.Value)
                return BadRequest("Cookbook is private—only owner can add.");

            // Use AnyAsync for existence check — avoids loading the full Recipe entity graph.
            if (!await _context.Recipes.AnyAsync(r => r.Id == dto.RecipeId))
                return NotFound("Recipe not found.");

            // prevent dup if already present in the cookbook
            var inCookbook = await _context.CookbookRecipes.AnyAsync(cr => cr.CookbookId == cookbookId && cr.RecipeId == dto.RecipeId);
            if (inCookbook) return BadRequest("Recipe already in this cookbook.");

            // prevent duplicate PENDING requests by the same user for the same recipe
            var existingPending = await _context.CookbookContributionRequests.AnyAsync(r =>
                r.CookbookId == cookbookId &&
                r.RecipeId == dto.RecipeId &&
                r.RequestedByUserId == requesterId.Value &&
                r.Status == ContributionStatus.Pending);

            if (existingPending)
                return BadRequest("You already have a pending request for this recipe.");

            var request = new CookbookContributionRequest
            {
                CookbookId = cookbookId,
                RecipeId = dto.RecipeId,
                RequestedByUserId = requesterId.Value,
                Message = string.IsNullOrWhiteSpace(dto.Message) ? null : dto.Message.Trim(),
                Status = ContributionStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            _context.CookbookContributionRequests.Add(request);
            await _context.SaveChangesAsync();

            // Notify the cookbook owner about the new request
            await _notifications.NotifyCookbookRecipeRequestAsync(cb.UserId, requesterId.Value, cookbookId, dto.RecipeId);

            return Ok(new { request.Id });
        }

        /// <summary>
        /// List contribution requests for a cookbook, optionally filtered by status
        /// (<c>Pending</c>, <c>Approved</c>, <c>Rejected</c>). Owner-only.
        /// Results are ordered by status then descending by creation date.
        /// </summary>
        /// <param name="cookbookId">The cookbook GUID.</param>
        /// <param name="status">Optional <see cref="ContributionStatus"/> filter.</param>
        /// <returns>
        /// <c>200 OK</c> — array of <see cref="ContributionRequestDto"/>.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>403</c> — caller is not the cookbook owner.<br/>
        /// <c>404</c> — cookbook not found.
        /// </returns>
        // Owner: list pending/approved/rejected
        [Authorize]
        [HttpGet("{cookbookId:guid}/requests")]
        public async Task<IActionResult> ListRequests(Guid cookbookId, [FromQuery] ContributionStatus? status = null)
        {
            var uid = CurrentUserId();
            if (uid is null) return Unauthorized();

            var cb = await _context.Cookbooks.FirstOrDefaultAsync(c => c.Id == cookbookId);
            if (cb == null) return NotFound("Cookbook not found.");
            if (cb.UserId != uid.Value) return Forbid(); // owner-only

            var q = _context.CookbookContributionRequests.Where(r => r.CookbookId == cookbookId);
            if (status.HasValue) q = q.Where(r => r.Status == status.Value);

            var list = await q
                .OrderBy(r => r.Status)
                .ThenByDescending(r => r.CreatedAt)
                .Select(r => new ContributionRequestDto
                {
                    Id = r.Id,
                    CookbookId = r.CookbookId,
                    RecipeId = r.RecipeId,
                    RequestedByUserId = r.RequestedByUserId,
                    Message = r.Message,
                    Status = r.Status.ToString(),
                    CreatedAt = r.CreatedAt,
                    ReviewedAt = r.ReviewedAt
                })
                .ToListAsync();

            return Ok(list);
        }

        /// <summary>
        /// Approve a pending contribution request, atomically adding the proposed recipe to the
        /// cookbook (appended at the end) and marking the request <c>Approved</c>. If the recipe
        /// is already in the cookbook the add is skipped but the request is still approved.
        /// The entire operation runs inside a DB transaction. The requester is notified via
        /// <see cref="INotificationService.NotifyCookbookRequestApprovedAsync"/>. Owner-only.
        /// Requires subscription/trial.
        /// </summary>
        /// <param name="cookbookId">The cookbook GUID.</param>
        /// <param name="requestId">The contribution request GUID to approve.</param>
        /// <returns>
        /// <c>204 No Content</c> on success.<br/>
        /// <c>400</c> — request is not in Pending status.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>403</c> — no subscription/trial or caller is not the owner.<br/>
        /// <c>404</c> — cookbook or request not found.
        /// </returns>
        // Owner: approve (adds to cookbook)
        [Authorize]
        [HttpPost("{cookbookId:guid}/requests/{requestId:guid}/approve")]
        public async Task<IActionResult> ApproveRequest(Guid cookbookId, Guid requestId)
        {
            var uid = CurrentUserId();
            if (uid is null) return Unauthorized();

            // 🔐 SUBSCRIPTION CHECK
            var forbid = await ForbidIfNoCookbookAccess(uid.Value);
            if (forbid != null) return forbid;

            var cb = await _context.Cookbooks.FirstOrDefaultAsync(c => c.Id == cookbookId);
            if (cb == null) return NotFound("Cookbook not found.");
            if (cb.UserId != uid.Value) return Forbid(); // owner-only

            await using var tx = await _context.Database.BeginTransactionAsync();

            var req = await _context.CookbookContributionRequests
                .FirstOrDefaultAsync(r => r.Id == requestId && r.CookbookId == cookbookId);

            if (req == null) return NotFound("Request not found.");
            if (req.Status != ContributionStatus.Pending) return BadRequest("Request is not pending.");

            var exists = await _context.CookbookRecipes.AnyAsync(cr => cr.CookbookId == cookbookId && cr.RecipeId == req.RecipeId);
            if (!exists)
            {
                var max = await _context.CookbookRecipes
                    .Where(cr => cr.CookbookId == cookbookId)
                    .Select(cr => (int?)cr.SortOrder)
                    .MaxAsync();
                var sortOrder = (max ?? -1) + 1;

                _context.CookbookRecipes.Add(new CookbookRecipe
                {
                    CookbookId = cookbookId,
                    RecipeId = req.RecipeId,
                    SortOrder = sortOrder,
                    Note = null,
                    AddedByUserId = req.RequestedByUserId,
                    AddedAt = DateTime.UtcNow
                });
            }

            req.Status = ContributionStatus.Approved;
            req.ReviewedByUserId = uid.Value;
            req.ReviewedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            // Notify the requester that their request was approved
            await _notifications.NotifyCookbookRequestApprovedAsync(req.RequestedByUserId, uid.Value, cookbookId, req.RecipeId);

            return NoContent();
        }

        /// <summary>
        /// Reject a pending contribution request, marking it <c>Rejected</c> without adding the
        /// recipe to the cookbook. The requester is notified via
        /// <see cref="INotificationService.NotifyCookbookRequestRejectedAsync"/>. Owner-only.
        /// </summary>
        /// <param name="cookbookId">The cookbook GUID.</param>
        /// <param name="requestId">The contribution request GUID to reject.</param>
        /// <returns>
        /// <c>204 No Content</c> on success.<br/>
        /// <c>400</c> — request is not in Pending status.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>403</c> — caller is not the cookbook owner.<br/>
        /// <c>404</c> — cookbook or request not found.
        /// </returns>
        // Owner: reject
        [Authorize]
        [HttpPost("{cookbookId:guid}/requests/{requestId:guid}/reject")]
        public async Task<IActionResult> RejectRequest(Guid cookbookId, Guid requestId)
        {
            var uid = CurrentUserId();
            if (uid is null) return Unauthorized();

            var cb = await _context.Cookbooks.FirstOrDefaultAsync(c => c.Id == cookbookId);
            if (cb == null) return NotFound("Cookbook not found.");
            if (cb.UserId != uid.Value) return Forbid(); // owner-only

            var req = await _context.CookbookContributionRequests.FirstOrDefaultAsync(r => r.Id == requestId && r.CookbookId == cookbookId);
            if (req == null) return NotFound("Request not found.");
            if (req.Status != ContributionStatus.Pending) return BadRequest("Request is not pending.");

            req.Status = ContributionStatus.Rejected;
            req.ReviewedByUserId = uid.Value;
            req.ReviewedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Notify the requester that their request was rejected
            await _notifications.NotifyCookbookRequestRejectedAsync(req.RequestedByUserId, uid.Value, cookbookId, req.RecipeId);

            return NoContent();
        }
    }
}
