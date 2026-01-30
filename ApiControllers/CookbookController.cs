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

            var recipe = await _context.Recipes.FirstOrDefaultAsync(r => r.Id == dto.RecipeId);
            if (recipe == null) return NotFound("Recipe not found.");

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
                RecipeCount = items.Count
            };

            return Ok(result);
        }

        // ---------------------------
        // Contributors (owner-managed)
        // ---------------------------

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

            var recipe = await _context.Recipes.FirstOrDefaultAsync(r => r.Id == dto.RecipeId);
            if (recipe == null) return NotFound("Recipe not found.");

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
