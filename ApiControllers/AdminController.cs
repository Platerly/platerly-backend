using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;
using Platerly.Api.Services;
using Platerly.Api.Services.Admin;
using Platerly.Api.Services.Ingredients;
using System.Security.Claims;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// Admin-layer controller for managing the Recipzee content safety infrastructure:
    /// prohibited ingredient keywords, ingredient categories (taxonomy), global ingredient
    /// records, and read-only audit log access.
    /// Base route: <c>api/admin</c>.
    /// Auth: JWT with <c>Admin</c> or <c>SuperAdmin</c> role. Audit log endpoints are
    /// restricted further to <c>SuperAdmin</c> only.
    /// Every mutating action is persisted to <c>AdminAuditLogs</c> via <see cref="IAdminAuditService"/>.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class AdminController : ControllerBase
    {
        private readonly PlaterlyDbContext _db;
        private readonly IAdminAuditService _audit;
        private readonly IBlobService _blobService;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            PlaterlyDbContext db,
            IAdminAuditService audit,
            IBlobService blobService,
            ILogger<AdminController> logger)
        {
            _db = db;
            _audit = audit;
            _blobService = blobService;
            _logger = logger;
        }

        private Guid? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
        }

        private string? GetIpAddress() => HttpContext.Connection.RemoteIpAddress?.ToString();
        private string? GetUserAgent() => HttpContext.Request.Headers["User-Agent"].ToString();

        // ========== Prohibited Ingredients ==========

        /// <summary>
        /// Retrieve a paginated, filterable list of prohibited ingredient keywords used by the
        /// ingredient validation safety layer. Supports filtering by category
        /// (Haram, Prohibited, Dangerous, etc.), keyword search, and active/inactive status.
        /// </summary>
        /// <param name="category">Optional <see cref="ProhibitedCategory"/> enum name filter.</param>
        /// <param name="search">Optional keyword substring search.</param>
        /// <param name="isActive">Optional active status filter.</param>
        /// <param name="page">1-based page number (default 1).</param>
        /// <param name="pageSize">Results per page, clamped 1–200 (default 50).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="ProhibitedIngredientListResponse"/> containing items,
        /// total count, pagination info, and per-category counts.
        /// </returns>
        [HttpGet("prohibited-ingredients")]
        public async Task<ActionResult<ProhibitedIngredientListResponse>> GetProhibitedIngredients(
            [FromQuery] string? category = null,
            [FromQuery] string? search = null,
            [FromQuery] bool? isActive = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken ct = default)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var query = _db.ProhibitedIngredients.AsQueryable();

            if (!string.IsNullOrEmpty(category) && Enum.TryParse<ProhibitedCategory>(category, true, out var cat))
            {
                query = query.Where(p => p.Category == cat);
            }

            if (!string.IsNullOrEmpty(search))
            {
                var searchLower = search.ToLower();
                query = query.Where(p => p.Keyword.ToLower().Contains(searchLower));
            }

            if (isActive.HasValue)
            {
                query = query.Where(p => p.IsActive == isActive.Value);
            }

            var totalCount = await query.CountAsync(ct);

            var items = await query
                .OrderBy(p => p.Category)
                .ThenBy(p => p.Keyword)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new ProhibitedIngredientDto
                {
                    Id = p.Id,
                    Keyword = p.Keyword,
                    Category = p.Category.ToString(),
                    MatchType = p.MatchType.ToString(),
                    Reason = p.Reason,
                    AdminNotes = p.AdminNotes,
                    Severity = p.Severity.ToString(),
                    IsActive = p.IsActive,
                    CreatedAt = p.CreatedAt,
                    CreatedByUserId = p.CreatedByUserId
                })
                .AsNoTracking()
                .ToListAsync(ct);

            var categoryCounts = await _db.ProhibitedIngredients
                .GroupBy(p => p.Category)
                .Select(g => new { Category = g.Key.ToString(), Count = g.Count() })
                .ToDictionaryAsync(x => x.Category, x => x.Count, ct);

            return Ok(new ProhibitedIngredientListResponse
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                CategoryCounts = categoryCounts
            });
        }

        /// <summary>
        /// Retrieve a single prohibited ingredient record by its integer primary key.
        /// </summary>
        /// <param name="id">The prohibited ingredient ID.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="ProhibitedIngredientDto"/>.<br/>
        /// <c>404</c> — record not found.
        /// </returns>
        [HttpGet("prohibited-ingredients/{id:int}")]
        public async Task<ActionResult<ProhibitedIngredientDto>> GetProhibitedIngredient(
            int id,
            CancellationToken ct = default)
        {
            var item = await _db.ProhibitedIngredients
                .Where(p => p.Id == id)
                .Select(p => new ProhibitedIngredientDto
                {
                    Id = p.Id,
                    Keyword = p.Keyword,
                    Category = p.Category.ToString(),
                    MatchType = p.MatchType.ToString(),
                    Reason = p.Reason,
                    AdminNotes = p.AdminNotes,
                    Severity = p.Severity.ToString(),
                    IsActive = p.IsActive,
                    CreatedAt = p.CreatedAt,
                    CreatedByUserId = p.CreatedByUserId
                })
                .FirstOrDefaultAsync(ct);

            if (item == null)
            {
                return NotFound();
            }

            return Ok(item);
        }

        /// <summary>
        /// Add a new prohibited ingredient keyword to the safety database.
        /// Duplicate keywords (case-insensitive) are rejected and the attempt is audit logged.
        /// Invalid enum values for category, matchType, and severity default to
        /// <c>Haram</c>, <c>Contains</c>, and <c>Block</c> respectively.
        /// </summary>
        /// <param name="dto">Keyword, category, match type, reason, severity, and optional admin notes.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>201 Created</c> — the created <see cref="ProhibitedIngredientDto"/>.<br/>
        /// <c>400</c> — duplicate keyword.<br/>
        /// <c>401</c> — unauthenticated.
        /// </returns>
        [HttpPost("prohibited-ingredients")]
        public async Task<ActionResult<ProhibitedIngredientDto>> CreateProhibitedIngredient(
            [FromBody] CreateProhibitedIngredientDto dto,
            CancellationToken ct = default)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return Unauthorized();

            var keywordLower = dto.Keyword.Trim().ToLowerInvariant();

            // Check for duplicate
            var exists = await _db.ProhibitedIngredients
                .AnyAsync(p => p.Keyword.ToLower() == keywordLower, ct);

            if (exists)
            {
                await _audit.LogFailureAsync(
                    userId.Value,
                    AdminAction.CreateProhibitedIngredient,
                    "ProhibitedIngredient",
                    null,
                    $"Attempted to create duplicate: {dto.Keyword}",
                    "Keyword already exists",
                    GetIpAddress(),
                    GetUserAgent(),
                    ct);

                return BadRequest($"Prohibited ingredient with keyword '{dto.Keyword}' already exists");
            }

            // Parse enums
            if (!Enum.TryParse<ProhibitedCategory>(dto.Category, true, out var category))
            {
                category = ProhibitedCategory.Haram;
            }

            if (!Enum.TryParse<Models.MatchType>(dto.MatchType, true, out var matchType))
            {
                matchType = Models.MatchType.Contains;
            }

            if (!Enum.TryParse<ViolationSeverity>(dto.Severity, true, out var severity))
            {
                severity = ViolationSeverity.Block;
            }

            var item = new ProhibitedIngredient
            {
                Keyword = dto.Keyword.Trim(),
                Category = category,
                MatchType = matchType,
                Reason = dto.Reason,
                AdminNotes = dto.AdminNotes,
                Severity = severity,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = userId
            };

            _db.ProhibitedIngredients.Add(item);
            await _db.SaveChangesAsync(ct);

            // Audit log
            await _audit.LogAsync(
                userId.Value,
                AdminAction.CreateProhibitedIngredient,
                "ProhibitedIngredient",
                item.Id.ToString(),
                $"Created prohibited ingredient: {item.Keyword} ({item.Category})",
                null,
                new { item.Keyword, Category = item.Category.ToString(), Severity = item.Severity.ToString() },
                GetIpAddress(),
                GetUserAgent(),
                ct);

            _logger.LogInformation("Created prohibited ingredient: {Keyword} ({Category})",
                item.Keyword, item.Category);

            return CreatedAtAction(
                nameof(GetProhibitedIngredient),
                new { id = item.Id },
                new ProhibitedIngredientDto
                {
                    Id = item.Id,
                    Keyword = item.Keyword,
                    Category = item.Category.ToString(),
                    MatchType = item.MatchType.ToString(),
                    Reason = item.Reason,
                    AdminNotes = item.AdminNotes,
                    Severity = item.Severity.ToString(),
                    IsActive = item.IsActive,
                    CreatedAt = item.CreatedAt,
                    CreatedByUserId = item.CreatedByUserId
                });
        }

        /// <summary>
        /// Update an existing prohibited ingredient record. Only provided (non-null) fields are
        /// changed (patch semantics). Old and new values are captured in the audit log.
        /// </summary>
        /// <param name="id">The prohibited ingredient ID to update.</param>
        /// <param name="dto">Fields to update.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — updated <see cref="ProhibitedIngredientDto"/>.<br/>
        /// <c>400</c> — duplicate keyword conflict.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — record not found.
        /// </returns>
        [HttpPut("prohibited-ingredients/{id:int}")]
        public async Task<ActionResult<ProhibitedIngredientDto>> UpdateProhibitedIngredient(
            int id,
            [FromBody] UpdateProhibitedIngredientDto dto,
            CancellationToken ct = default)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return Unauthorized();

            var item = await _db.ProhibitedIngredients.FindAsync(new object[] { id }, ct);

            if (item == null)
            {
                return NotFound();
            }

            // Capture old values for audit
            var oldValues = new
            {
                item.Keyword,
                Category = item.Category.ToString(),
                MatchType = item.MatchType.ToString(),
                item.Reason,
                Severity = item.Severity.ToString(),
                item.IsActive
            };

            if (!string.IsNullOrEmpty(dto.Keyword))
            {
                var keywordLower = dto.Keyword.Trim().ToLowerInvariant();
                var exists = await _db.ProhibitedIngredients
                    .AnyAsync(p => p.Id != id && p.Keyword.ToLower() == keywordLower, ct);

                if (exists)
                {
                    return BadRequest($"Prohibited ingredient with keyword '{dto.Keyword}' already exists");
                }

                item.Keyword = dto.Keyword.Trim();
            }

            if (!string.IsNullOrEmpty(dto.Category) && Enum.TryParse<ProhibitedCategory>(dto.Category, true, out var category))
            {
                item.Category = category;
            }

            if (!string.IsNullOrEmpty(dto.MatchType) && Enum.TryParse<Models.MatchType>(dto.MatchType, true, out var matchType))
            {
                item.MatchType = matchType;
            }

            if (dto.Reason != null)
                item.Reason = dto.Reason;

            if (dto.AdminNotes != null)
                item.AdminNotes = dto.AdminNotes;

            if (!string.IsNullOrEmpty(dto.Severity) && Enum.TryParse<ViolationSeverity>(dto.Severity, true, out var severity))
            {
                item.Severity = severity;
            }

            if (dto.IsActive.HasValue)
                item.IsActive = dto.IsActive.Value;

            await _db.SaveChangesAsync(ct);

            // Audit log
            var newValues = new
            {
                item.Keyword,
                Category = item.Category.ToString(),
                MatchType = item.MatchType.ToString(),
                item.Reason,
                Severity = item.Severity.ToString(),
                item.IsActive
            };

            await _audit.LogAsync(
                userId.Value,
                AdminAction.UpdateProhibitedIngredient,
                "ProhibitedIngredient",
                id.ToString(),
                $"Updated prohibited ingredient: {item.Keyword}",
                oldValues,
                newValues,
                GetIpAddress(),
                GetUserAgent(),
                ct);

            _logger.LogInformation("Updated prohibited ingredient {Id}: {Keyword}", id, item.Keyword);

            return Ok(new ProhibitedIngredientDto
            {
                Id = item.Id,
                Keyword = item.Keyword,
                Category = item.Category.ToString(),
                MatchType = item.MatchType.ToString(),
                Reason = item.Reason,
                AdminNotes = item.AdminNotes,
                Severity = item.Severity.ToString(),
                IsActive = item.IsActive,
                CreatedAt = item.CreatedAt,
                CreatedByUserId = item.CreatedByUserId
            });
        }

        /// <summary>
        /// Permanently (hard) delete a prohibited ingredient from the safety database.
        /// This action is irreversible. The deletion is audit logged.
        /// </summary>
        /// <param name="id">The prohibited ingredient ID to delete.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>204 No Content</c> on success.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — record not found.
        /// </returns>
        [HttpDelete("prohibited-ingredients/{id:int}")]
        public async Task<IActionResult> DeleteProhibitedIngredient(
            int id,
            CancellationToken ct = default)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return Unauthorized();

            var item = await _db.ProhibitedIngredients.FindAsync(new object[] { id }, ct);

            if (item == null)
            {
                return NotFound();
            }

            var oldValues = new
            {
                item.Keyword,
                Category = item.Category.ToString(),
                item.Reason
            };

            _db.ProhibitedIngredients.Remove(item);
            await _db.SaveChangesAsync(ct);

            // Audit log
            await _audit.LogAsync(
                userId.Value,
                AdminAction.DeleteProhibitedIngredient,
                "ProhibitedIngredient",
                id.ToString(),
                $"Deleted prohibited ingredient: {item.Keyword}",
                oldValues,
                null,
                GetIpAddress(),
                GetUserAgent(),
                ct);

            _logger.LogInformation("Deleted prohibited ingredient {Id}: {Keyword}", id, item.Keyword);

            return NoContent();
        }

        /// <summary>
        /// Bulk-create multiple prohibited ingredient records in a single request.
        /// Items whose keyword already exists (case-insensitive) are silently skipped;
        /// the response includes counts of created and skipped items with the skipped keywords listed.
        /// The entire batch is audit logged as one action.
        /// </summary>
        /// <param name="dto">Contains an <c>Items</c> array of create DTOs.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="BulkCreateResult"/> (<c>created</c>, <c>skipped</c>,
        /// <c>skippedKeywords</c>).<br/>
        /// <c>401</c> — unauthenticated.
        /// </returns>
        [HttpPost("prohibited-ingredients/bulk")]
        public async Task<ActionResult<BulkCreateResult>> BulkCreateProhibitedIngredients(
            [FromBody] BulkCreateProhibitedDto dto,
            CancellationToken ct = default)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return Unauthorized();

            var result = new BulkCreateResult();

            // Get existing keywords (lowercase)
            var existingKeywords = await _db.ProhibitedIngredients
                .Select(p => p.Keyword.ToLower())
                .ToListAsync(ct);
            var existingSet = existingKeywords.ToHashSet();

            var toCreate = new List<ProhibitedIngredient>();

            foreach (var itemDto in dto.Items)
            {
                var keywordLower = itemDto.Keyword.Trim().ToLowerInvariant();

                if (existingSet.Contains(keywordLower))
                {
                    result.Skipped++;
                    result.SkippedKeywords.Add(itemDto.Keyword);
                    continue;
                }

                if (!Enum.TryParse<ProhibitedCategory>(itemDto.Category, true, out var category))
                    category = ProhibitedCategory.Haram;

                if (!Enum.TryParse<Models.MatchType>(itemDto.MatchType, true, out var matchType))
                    matchType = Models.MatchType.Contains;

                if (!Enum.TryParse<ViolationSeverity>(itemDto.Severity, true, out var severity))
                    severity = ViolationSeverity.Block;

                toCreate.Add(new ProhibitedIngredient
                {
                    Keyword = itemDto.Keyword.Trim(),
                    Category = category,
                    MatchType = matchType,
                    Reason = itemDto.Reason,
                    AdminNotes = itemDto.AdminNotes,
                    Severity = severity,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    CreatedByUserId = userId
                });

                existingSet.Add(keywordLower);
            }

            if (toCreate.Any())
            {
                await _db.ProhibitedIngredients.AddRangeAsync(toCreate, ct);
                await _db.SaveChangesAsync(ct);
                result.Created = toCreate.Count;
            }

            // Audit log
            await _audit.LogAsync(
                userId.Value,
                AdminAction.BulkCreateProhibitedIngredients,
                "ProhibitedIngredient",
                null,
                $"Bulk created {result.Created} prohibited ingredients, skipped {result.Skipped}",
                null,
                new { Created = result.Created, Skipped = result.Skipped, Keywords = toCreate.Select(x => x.Keyword).ToList() },
                GetIpAddress(),
                GetUserAgent(),
                ct);

            _logger.LogInformation("Bulk created {Created} prohibited ingredients, skipped {Skipped}",
                result.Created, result.Skipped);

            return Ok(result);
        }

        // ========== Ingredient Categories ==========

        /// <summary>
        /// Retrieve the full ingredient category taxonomy, including parent/child relationships,
        /// icon names, dietary flags, and per-category ingredient counts.
        /// Results are ordered: top-level categories first, then by <c>SortOrder</c>, then name.
        /// </summary>
        /// <param name="isActive">Optional filter to return only active or inactive categories.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>200 OK</c> — array of <see cref="IngredientCategoryDto"/>.</returns>
        [HttpGet("categories")]
        public async Task<ActionResult<List<IngredientCategoryDto>>> GetCategories(
            [FromQuery] bool? isActive = null,
            CancellationToken ct = default)
        {
            var query = _db.IngredientCategories
                .Include(c => c.ParentCategory)
                .Include(c => c.ChildCategories)
                .Include(c => c.Ingredients)
                .AsQueryable();

            if (isActive.HasValue)
            {
                query = query.Where(c => c.IsActive == isActive.Value);
            }

            var categories = await query
                .OrderBy(c => c.ParentCategoryId == null ? 0 : 1)
                .ThenBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .Select(c => new IngredientCategoryDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Slug = c.Slug,
                    Description = c.Description,
                    ParentCategoryId = c.ParentCategoryId,
                    ParentCategoryName = c.ParentCategory != null ? c.ParentCategory.Name : null,
                    IconName = c.IconName,
                    SortOrder = c.SortOrder,
                    IsActive = c.IsActive,
                    CommonAllergens = c.CommonAllergens,
                    DietaryFlags = c.DietaryFlags,
                    ChildCount = c.ChildCategories.Count,
                    IngredientCount = c.Ingredients.Count
                })
                .AsNoTracking()
                .ToListAsync(ct);

            return Ok(categories);
        }

        /// <summary>
        /// Create a new ingredient category node in the taxonomy tree.
        /// The <c>Slug</c> must be unique; duplicate slugs are rejected.
        /// The new category starts as active. Action is audit logged.
        /// </summary>
        /// <param name="dto">Category details: name, slug, description, parentCategoryId, iconName,
        /// sortOrder, commonAllergens, dietaryFlags.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>201 Created</c> — the created <see cref="IngredientCategoryDto"/>.<br/>
        /// <c>400</c> — slug already exists.<br/>
        /// <c>401</c> — unauthenticated.
        /// </returns>
        [HttpPost("categories")]
        public async Task<ActionResult<IngredientCategoryDto>> CreateCategory(
            [FromBody] CreateIngredientCategoryDto dto,
            CancellationToken ct = default)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return Unauthorized();

            var exists = await _db.IngredientCategories
                .AnyAsync(c => c.Slug == dto.Slug, ct);

            if (exists)
            {
                return BadRequest($"Category with slug '{dto.Slug}' already exists");
            }

            var category = new IngredientCategory
            {
                Name = dto.Name,
                Slug = dto.Slug,
                Description = dto.Description,
                ParentCategoryId = dto.ParentCategoryId,
                IconName = dto.IconName,
                SortOrder = dto.SortOrder,
                CommonAllergens = dto.CommonAllergens,
                DietaryFlags = dto.DietaryFlags,
                IsActive = true
            };

            _db.IngredientCategories.Add(category);
            await _db.SaveChangesAsync(ct);

            // Audit log
            await _audit.LogAsync(
                userId.Value,
                AdminAction.CreateCategory,
                "IngredientCategory",
                category.Id.ToString(),
                $"Created category: {category.Name} ({category.Slug})",
                null,
                new { category.Name, category.Slug, category.ParentCategoryId },
                GetIpAddress(),
                GetUserAgent(),
                ct);

            _logger.LogInformation("Created ingredient category: {Name} ({Slug})",
                category.Name, category.Slug);

            return CreatedAtAction(
                nameof(GetCategories),
                null,
                new IngredientCategoryDto
                {
                    Id = category.Id,
                    Name = category.Name,
                    Slug = category.Slug,
                    Description = category.Description,
                    ParentCategoryId = category.ParentCategoryId,
                    IconName = category.IconName,
                    SortOrder = category.SortOrder,
                    IsActive = category.IsActive,
                    CommonAllergens = category.CommonAllergens,
                    DietaryFlags = category.DietaryFlags,
                    ChildCount = 0,
                    IngredientCount = 0
                });
        }

        /// <summary>
        /// Update an existing ingredient category. Only provided (non-null) fields are changed.
        /// Slug changes are validated for uniqueness across other categories. Action is audit logged.
        /// </summary>
        /// <param name="id">The category integer ID.</param>
        /// <param name="dto">Fields to update (patch semantics).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — updated <see cref="IngredientCategoryDto"/>.<br/>
        /// <c>400</c> — slug conflict.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — category not found.
        /// </returns>
        [HttpPut("categories/{id:int}")]
        public async Task<ActionResult<IngredientCategoryDto>> UpdateCategory(
            int id,
            [FromBody] UpdateIngredientCategoryDto dto,
            CancellationToken ct = default)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return Unauthorized();

            var category = await _db.IngredientCategories
                .Include(c => c.ParentCategory)
                .Include(c => c.ChildCategories)
                .Include(c => c.Ingredients)
                .FirstOrDefaultAsync(c => c.Id == id, ct);

            if (category == null)
            {
                return NotFound();
            }

            var oldValues = new { category.Name, category.Slug, category.IsActive };

            if (!string.IsNullOrEmpty(dto.Slug) && dto.Slug != category.Slug)
            {
                var exists = await _db.IngredientCategories
                    .AnyAsync(c => c.Id != id && c.Slug == dto.Slug, ct);

                if (exists)
                {
                    return BadRequest($"Category with slug '{dto.Slug}' already exists");
                }

                category.Slug = dto.Slug;
            }

            if (!string.IsNullOrEmpty(dto.Name))
                category.Name = dto.Name;

            if (dto.Description != null)
                category.Description = dto.Description;

            if (dto.ParentCategoryId.HasValue)
                category.ParentCategoryId = dto.ParentCategoryId;

            if (dto.IconName != null)
                category.IconName = dto.IconName;

            if (dto.SortOrder.HasValue)
                category.SortOrder = dto.SortOrder.Value;

            if (dto.IsActive.HasValue)
                category.IsActive = dto.IsActive.Value;

            if (dto.CommonAllergens != null)
                category.CommonAllergens = dto.CommonAllergens;

            if (dto.DietaryFlags != null)
                category.DietaryFlags = dto.DietaryFlags;

            await _db.SaveChangesAsync(ct);

            // Audit log
            await _audit.LogAsync(
                userId.Value,
                AdminAction.UpdateCategory,
                "IngredientCategory",
                id.ToString(),
                $"Updated category: {category.Name}",
                oldValues,
                new { category.Name, category.Slug, category.IsActive },
                GetIpAddress(),
                GetUserAgent(),
                ct);

            _logger.LogInformation("Updated ingredient category {Id}: {Name}", id, category.Name);

            return Ok(new IngredientCategoryDto
            {
                Id = category.Id,
                Name = category.Name,
                Slug = category.Slug,
                Description = category.Description,
                ParentCategoryId = category.ParentCategoryId,
                ParentCategoryName = category.ParentCategory?.Name,
                IconName = category.IconName,
                SortOrder = category.SortOrder,
                IsActive = category.IsActive,
                CommonAllergens = category.CommonAllergens,
                DietaryFlags = category.DietaryFlags,
                ChildCount = category.ChildCategories.Count,
                IngredientCount = category.Ingredients.Count
            });
        }

        // ========== Global Ingredients ==========

        /// <summary>
        /// Retrieve global ingredient records with full admin metadata (aliases, verification status,
        /// usage counts, dietary flags, merged state). Supports search, category, and status filters.
        /// Results are ordered by total usage count descending.
        /// </summary>
        /// <param name="search">Optional substring to match against name, normalizedName, or aliases.</param>
        /// <param name="categoryId">Optional category ID filter.</param>
        /// <param name="isVerified">Optional verification status filter.</param>
        /// <param name="isActive">Optional active status filter.</param>
        /// <param name="page">1-based page number (default 1).</param>
        /// <param name="pageSize">Results per page, clamped 1–200 (default 50).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>200 OK</c> — array of <see cref="GlobalIngredientAdminDto"/>.</returns>
        [HttpGet("ingredients")]
        public async Task<ActionResult<List<GlobalIngredientAdminDto>>> GetIngredients(
            [FromQuery] string? search = null,
            [FromQuery] int? categoryId = null,
            [FromQuery] bool? isVerified = null,
            [FromQuery] bool? isActive = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken ct = default)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var query = _db.GlobalIngredients
                .Include(g => g.Category)
                .AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                var searchLower = search.ToLower();
                query = query.Where(g =>
                    g.Name.ToLower().Contains(searchLower) ||
                    g.NormalizedName.Contains(searchLower) ||
                    (g.Aliases != null && g.Aliases.ToLower().Contains(searchLower)));
            }

            if (categoryId.HasValue)
            {
                query = query.Where(g => g.CategoryId == categoryId);
            }

            if (isVerified.HasValue)
            {
                query = query.Where(g => g.IsVerified == isVerified.Value);
            }

            if (isActive.HasValue)
            {
                query = query.Where(g => g.IsActive == isActive.Value);
            }

            var ingredients = await query
                .OrderByDescending(g => g.TotalUsageCount)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(g => new GlobalIngredientAdminDto
                {
                    Id = g.Id,
                    Name = g.Name,
                    NormalizedName = g.NormalizedName,
                    Aliases = g.Aliases,
                    IsAllergenic = g.IsAllergenic,
                    CategoryId = g.CategoryId,
                    CategoryName = g.Category != null ? g.Category.Name : null,
                    TotalUsageCount = g.TotalUsageCount,
                    UniqueRecipeCount = g.UniqueRecipeCount,
                    PopularityRank = g.PopularityRank,
                    PrimaryRegion = g.PrimaryRegion,
                    DietaryFlags = g.DietaryFlags,
                    IsVerified = g.IsVerified,
                    IsActive = g.IsActive,
                    MergedIntoId = g.MergedIntoId,
                    CreatedAt = g.CreatedAt,
                    UpdatedAt = g.UpdatedAt
                })
                .AsNoTracking()
                .ToListAsync(ct);

            return Ok(ingredients);
        }

        /// <summary>
        /// Update a global ingredient's metadata (name, aliases, allergenic flag, category,
        /// dietary flags, verification status, etc.). Name changes automatically update the
        /// normalized name. Action is audit logged.
        /// </summary>
        /// <param name="id">The global ingredient GUID.</param>
        /// <param name="dto">Fields to update (patch semantics).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — updated <see cref="GlobalIngredientAdminDto"/>.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — ingredient not found.
        /// </returns>
        [HttpPut("ingredients/{id:guid}")]
        public async Task<ActionResult<GlobalIngredientAdminDto>> UpdateIngredient(
            Guid id,
            [FromBody] UpdateGlobalIngredientDto dto,
            CancellationToken ct = default)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return Unauthorized();

            var ingredient = await _db.GlobalIngredients
                .Include(g => g.Category)
                .FirstOrDefaultAsync(g => g.Id == id, ct);

            if (ingredient == null)
            {
                return NotFound();
            }

            var oldValues = new
            {
                ingredient.Name,
                ingredient.IsVerified,
                ingredient.IsActive,
                ingredient.CategoryId
            };

            if (!string.IsNullOrEmpty(dto.Name))
            {
                ingredient.Name = dto.Name;
                ingredient.NormalizedName = dto.Name.Trim().ToLowerInvariant();
            }

            if (dto.Aliases != null)
                ingredient.Aliases = dto.Aliases;

            if (dto.IsAllergenic.HasValue)
                ingredient.IsAllergenic = dto.IsAllergenic.Value;

            if (dto.CategoryId.HasValue)
                ingredient.CategoryId = dto.CategoryId;

            if (dto.PrimaryRegion != null)
                ingredient.PrimaryRegion = dto.PrimaryRegion;

            if (dto.DietaryFlags != null)
                ingredient.DietaryFlags = dto.DietaryFlags;

            if (dto.NutritionalCategory != null)
                ingredient.NutritionalCategory = dto.NutritionalCategory;

            if (dto.IsVerified.HasValue)
                ingredient.IsVerified = dto.IsVerified.Value;

            if (dto.IsActive.HasValue)
                ingredient.IsActive = dto.IsActive.Value;

            ingredient.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            // Audit log
            await _audit.LogAsync(
                userId.Value,
                AdminAction.UpdateGlobalIngredient,
                "GlobalIngredient",
                id.ToString(),
                $"Updated ingredient: {ingredient.Name}",
                oldValues,
                new { ingredient.Name, ingredient.IsVerified, ingredient.IsActive, ingredient.CategoryId },
                GetIpAddress(),
                GetUserAgent(),
                ct);

            _logger.LogInformation("Updated global ingredient {Id}: {Name}", id, ingredient.Name);

            return Ok(new GlobalIngredientAdminDto
            {
                Id = ingredient.Id,
                Name = ingredient.Name,
                NormalizedName = ingredient.NormalizedName,
                Aliases = ingredient.Aliases,
                IsAllergenic = ingredient.IsAllergenic,
                CategoryId = ingredient.CategoryId,
                CategoryName = ingredient.Category?.Name,
                TotalUsageCount = ingredient.TotalUsageCount,
                UniqueRecipeCount = ingredient.UniqueRecipeCount,
                PopularityRank = ingredient.PopularityRank,
                PrimaryRegion = ingredient.PrimaryRegion,
                DietaryFlags = ingredient.DietaryFlags,
                IsVerified = ingredient.IsVerified,
                IsActive = ingredient.IsActive,
                MergedIntoId = ingredient.MergedIntoId,
                CreatedAt = ingredient.CreatedAt,
                UpdatedAt = ingredient.UpdatedAt
            });
        }

        /// <summary>
        /// Merge a duplicate ingredient record into a canonical target. All recipe-ingredient rows
        /// referencing the source are re-pointed to the target. Usage counts and aliases are
        /// transferred. The source is marked inactive with <c>MergedIntoId</c> set. Irreversible.
        /// Action is audit logged.
        /// </summary>
        /// <param name="dto">Contains <c>SourceIngredientId</c> (duplicate) and
        /// <c>TargetIngredientId</c> (canonical).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ message, sourceId, targetId }</c>.<br/>
        /// <c>400</c> — source and target are the same.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — one or both ingredients not found.
        /// </returns>
        [HttpPost("ingredients/merge")]
        public async Task<IActionResult> MergeIngredients(
            [FromBody] MergeIngredientsDto dto,
            CancellationToken ct = default)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return Unauthorized();

            var source = await _db.GlobalIngredients.FindAsync(new object[] { dto.SourceIngredientId }, ct);
            var target = await _db.GlobalIngredients.FindAsync(new object[] { dto.TargetIngredientId }, ct);

            if (source == null || target == null)
            {
                return NotFound("One or both ingredients not found");
            }

            if (source.Id == target.Id)
            {
                return BadRequest("Cannot merge ingredient into itself");
            }

            var oldValues = new
            {
                SourceName = source.Name,
                SourceUsageCount = source.TotalUsageCount,
                TargetName = target.Name,
                TargetUsageCount = target.TotalUsageCount
            };

            // Update all recipe ingredients that reference source to reference target
            await _db.Ingredients
                .Where(i => i.GlobalIngredientId == source.Id)
                .ExecuteUpdateAsync(i => i.SetProperty(x => x.GlobalIngredientId, target.Id), ct);

            // Transfer usage counts
            target.TotalUsageCount += source.TotalUsageCount;
            target.UniqueRecipeCount += source.UniqueRecipeCount;

            // Merge aliases
            if (!string.IsNullOrEmpty(source.Aliases))
            {
                target.Aliases = string.IsNullOrEmpty(target.Aliases)
                    ? $"{source.Name},{source.Aliases}"
                    : $"{target.Aliases},{source.Name},{source.Aliases}";
            }
            else
            {
                target.Aliases = string.IsNullOrEmpty(target.Aliases)
                    ? source.Name
                    : $"{target.Aliases},{source.Name}";
            }

            // Mark source as merged
            source.IsActive = false;
            source.MergedIntoId = target.Id;
            source.UpdatedAt = DateTime.UtcNow;
            target.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            // Audit log
            await _audit.LogAsync(
                userId.Value,
                AdminAction.MergeIngredients,
                "GlobalIngredient",
                $"{source.Id}->{target.Id}",
                $"Merged '{source.Name}' into '{target.Name}'",
                oldValues,
                new { TargetName = target.Name, TargetUsageCount = target.TotalUsageCount, TargetAliases = target.Aliases },
                GetIpAddress(),
                GetUserAgent(),
                ct);

            _logger.LogInformation("Merged ingredient {SourceId} ({SourceName}) into {TargetId} ({TargetName})",
                source.Id, source.Name, target.Id, target.Name);

            return Ok(new
            {
                message = $"Merged '{source.Name}' into '{target.Name}'",
                sourceId = source.Id,
                targetId = target.Id
            });
        }

        // ========== Audit Logs ==========

        /// <summary>
        /// Retrieve paginated admin audit logs. Restricted to <c>SuperAdmin</c> only.
        /// Supports filtering by performing admin user, action type, entity type, and date range.
        /// </summary>
        /// <param name="adminUserId">Optional — filter by the admin who performed the action.</param>
        /// <param name="action">Optional — filter by action name (e.g., "CreateProhibitedIngredient").</param>
        /// <param name="entityType">Optional — filter by affected entity type (e.g., "GlobalIngredient").</param>
        /// <param name="fromDate">Optional — inclusive start of date range (UTC).</param>
        /// <param name="toDate">Optional — inclusive end of date range (UTC).</param>
        /// <param name="page">1-based page number (default 1).</param>
        /// <param name="pageSize">Results per page, clamped 1–200 (default 50).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — paginated audit log object (items, totalCount, page, pageSize).<br/>
        /// <c>403</c> — caller is not SuperAdmin.
        /// </returns>
        [HttpGet("audit-logs")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<object>> GetAuditLogs(
            [FromQuery] Guid? adminUserId = null,
            [FromQuery] string? action = null,
            [FromQuery] string? entityType = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken ct = default)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            AdminAction? parsedAction = null;
            if (!string.IsNullOrEmpty(action) && Enum.TryParse<AdminAction>(action, true, out var a))
            {
                parsedAction = a;
            }

            var logs = await _audit.GetLogsAsync(
                adminUserId, parsedAction, entityType, fromDate, toDate, page, pageSize, ct);

            var totalCount = await _audit.GetLogCountAsync(
                adminUserId, parsedAction, entityType, fromDate, toDate, ct);

            return Ok(new
            {
                items = logs.Select(l => new
                {
                    l.Id,
                    l.AdminUserId,
                    AdminUsername = l.AdminUser?.Username,
                    Action = l.Action.ToString(),
                    l.EntityType,
                    l.EntityId,
                    l.Description,
                    l.OldValues,
                    l.NewValues,
                    l.IpAddress,
                    l.CreatedAt,
                    l.WasSuccessful,
                    l.ErrorMessage
                }),
                totalCount,
                page,
                pageSize
            });
        }

        // ========== SAS Token Migration ==========

        /// <summary>
        /// Preview SAS token migration - shows what would be updated without making changes.
        /// </summary>
        [HttpGet("sas-migration/preview")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> PreviewSasMigration(CancellationToken ct = default)
        {
            var stats = new SasMigrationStats();

            // Count users with profile images needing migration
            stats.UsersWithProfileImages = await _db.Users
                .Where(u => u.ProfileImageBlobName != null && u.ProfileImageBlobName != "")
                .CountAsync(ct);

            // Count users with banner images needing migration
            stats.UsersWithBannerImages = await _db.Users
                .Where(u => u.BannerImageBlobName != null && u.BannerImageBlobName != "")
                .CountAsync(ct);

            // Count recipes with images needing migration
            stats.RecipesWithImages = await _db.Recipes
                .Where(r => r.ImageBlobName != null && r.ImageBlobName != "" && !r.IsDeleted)
                .CountAsync(ct);

            // Count cookbooks with cover images needing migration
            stats.CookbooksWithCovers = await _db.Cookbooks
                .Where(c => c.CoverImageBlobName != null && c.CoverImageBlobName != "")
                .CountAsync(ct);

            stats.TotalRecords = stats.UsersWithProfileImages +
                                 stats.UsersWithBannerImages +
                                 stats.RecipesWithImages +
                                 stats.CookbooksWithCovers;

            return Ok(new
            {
                message = "Preview of SAS token migration",
                stats,
                warning = "Run POST /api/admin/sas-migration/execute to perform the actual migration"
            });
        }

        /// <summary>
        /// Execute SAS token migration - generates new SAS URLs for all blob-stored images.
        /// This will update all ImageUrl fields with new SAS-signed URLs.
        /// SuperAdmin only due to database-wide impact.
        /// </summary>
        [HttpPost("sas-migration/execute")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> ExecuteSasMigration(
            [FromQuery] bool dryRun = true,
            CancellationToken ct = default)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return Unauthorized();

            var result = new SasMigrationResult();
            var errors = new List<string>();

            _logger.LogInformation("Starting SAS migration. DryRun: {DryRun}", dryRun);

            // 1. Migrate User Profile Images
            var usersWithProfiles = await _db.Users
                .Where(u => u.ProfileImageBlobName != null && u.ProfileImageBlobName != "")
                .ToListAsync(ct);

            foreach (var user in usersWithProfiles)
            {
                try
                {
                    var newUrl = await _blobService.GenerateProfileImageSasUrl(user.ProfileImageBlobName!);
                    if (newUrl != null)
                    {
                        if (!dryRun)
                        {
                            user.ProfileImageUrl = newUrl;
                        }
                        result.ProfileImagesUpdated++;
                    }
                    else
                    {
                        result.ProfileImagesMissing++;
                        errors.Add($"Profile image blob not found: {user.ProfileImageBlobName} (User: {user.Username})");
                    }
                }
                catch (Exception ex)
                {
                    result.ProfileImagesFailed++;
                    errors.Add($"Failed to generate SAS for profile {user.ProfileImageBlobName}: {ex.Message}");
                }
            }

            // 2. Migrate User Banner Images
            var usersWithBanners = await _db.Users
                .Where(u => u.BannerImageBlobName != null && u.BannerImageBlobName != "")
                .ToListAsync(ct);

            foreach (var user in usersWithBanners)
            {
                try
                {
                    var newUrl = await _blobService.GenerateBannerImageSasUrl(user.BannerImageBlobName!);
                    if (newUrl != null)
                    {
                        if (!dryRun)
                        {
                            user.BannerImageUrl = newUrl;
                        }
                        result.BannerImagesUpdated++;
                    }
                    else
                    {
                        result.BannerImagesMissing++;
                        errors.Add($"Banner image blob not found: {user.BannerImageBlobName} (User: {user.Username})");
                    }
                }
                catch (Exception ex)
                {
                    result.BannerImagesFailed++;
                    errors.Add($"Failed to generate SAS for banner {user.BannerImageBlobName}: {ex.Message}");
                }
            }

            // 3. Migrate Recipe Images
            var recipesWithImages = await _db.Recipes
                .Where(r => r.ImageBlobName != null && r.ImageBlobName != "" && !r.IsDeleted)
                .ToListAsync(ct);

            foreach (var recipe in recipesWithImages)
            {
                try
                {
                    var newUrl = await _blobService.GenerateRecipeImageSasUrl(recipe.ImageBlobName!);
                    if (newUrl != null)
                    {
                        if (!dryRun)
                        {
                            recipe.ImageUrl = newUrl;
                        }
                        result.RecipeImagesUpdated++;
                    }
                    else
                    {
                        result.RecipeImagesMissing++;
                        errors.Add($"Recipe image blob not found: {recipe.ImageBlobName} (Recipe: {recipe.Id})");
                    }
                }
                catch (Exception ex)
                {
                    result.RecipeImagesFailed++;
                    errors.Add($"Failed to generate SAS for recipe {recipe.ImageBlobName}: {ex.Message}");
                }
            }

            // 4. Migrate Cookbook Covers
            var cookbooksWithCovers = await _db.Cookbooks
                .Where(c => c.CoverImageBlobName != null && c.CoverImageBlobName != "")
                .ToListAsync(ct);

            foreach (var cookbook in cookbooksWithCovers)
            {
                try
                {
                    var newUrl = await _blobService.GenerateCookbookCoverSasUrl(cookbook.CoverImageBlobName!);
                    if (newUrl != null)
                    {
                        if (!dryRun)
                        {
                            cookbook.CoverImageUrl = newUrl;
                        }
                        result.CookbookCoversUpdated++;
                    }
                    else
                    {
                        result.CookbookCoversMissing++;
                        errors.Add($"Cookbook cover blob not found: {cookbook.CoverImageBlobName} (Cookbook: {cookbook.Id})");
                    }
                }
                catch (Exception ex)
                {
                    result.CookbookCoversFailed++;
                    errors.Add($"Failed to generate SAS for cookbook {cookbook.CoverImageBlobName}: {ex.Message}");
                }
            }

            // Save changes if not dry run
            if (!dryRun)
            {
                await _db.SaveChangesAsync(ct);

                // Audit log
                await _audit.LogAsync(
                    userId.Value,
                    AdminAction.SystemMigration,
                    "SASMigration",
                    null,
                    $"Executed SAS migration: {result.TotalUpdated} URLs updated",
                    null,
                    result,
                    GetIpAddress(),
                    GetUserAgent(),
                    ct);

                _logger.LogInformation(
                    "SAS migration completed. Profile: {Profile}, Recipe: {Recipe}, Cookbook: {Cookbook}",
                    result.ProfileImagesUpdated, result.RecipeImagesUpdated, result.CookbookCoversUpdated);
            }

            result.Errors = errors.Take(100).ToList(); // Limit errors in response

            return Ok(new
            {
                message = dryRun ? "Dry run completed - no changes made" : "Migration completed successfully",
                dryRun,
                result
            });
        }

        // ========== Recipe Seeder ==========

        /// <summary>
        /// Seed the database with halal-safe recipes from TheMealDB + Pexels images.
        /// Requires SuperAdmin role.
        /// </summary>
        [HttpPost("seed-substitutions")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> SeedSubstitutions(
            [FromServices] IngredientSubstitutionSeeder seeder,
            [FromQuery] bool force = false,
            CancellationToken ct = default)
        {
            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            _logger.LogInformation("Substitution seeding started by user {UserId}. Force: {Force}", userId, force);

            await seeder.SeedAsync(force, ct);

            var count = await _db.IngredientSubstitutions.CountAsync(ct);

            return Ok(new
            {
                message = $"Substitution seeding complete. Total in DB: {count}",
                force,
                count
            });
        }

        [HttpPost("seed-recipes")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> SeedRecipes(
            [FromServices] IRecipeSeederService seeder,
            [FromQuery] int count = 100,
            [FromQuery] bool dryRun = true,
            [FromQuery] bool updateImages = false)
        {
            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            _logger.LogInformation("Recipe seeding started by user {UserId}. Count: {Count}, DryRun: {DryRun}, UpdateImages: {UpdateImages}",
                userId, count, dryRun, updateImages);

            var result = await seeder.SeedRecipesAsync(count, dryRun, updateImages);

            // Audit log
            await _audit.LogAsync(
                userId.Value,
                AdminAction.SystemMigration,
                "RecipeSeeder",
                null,
                $"Recipe seeding: {result.RecipesCreated} created, {result.RecipesSkipped} skipped",
                null,
                result,
                GetIpAddress(),
                GetUserAgent());

            return Ok(new
            {
                message = dryRun ? "Dry run completed - no changes made" : $"Seeding completed: {result.RecipesCreated} recipes created",
                dryRun,
                result
            });
        }

        // ========== Recipe Slug Backfill ==========

        /// <summary>
        /// Generate slugs for all existing recipes that don't have one.
        /// This enables SEO-friendly URLs like /@username/recipe-slug
        /// SuperAdmin only.
        /// </summary>
        [HttpPost("backfill-slugs")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> BackfillSlugs(
            [FromQuery] bool dryRun = true,
            CancellationToken ct = default)
        {
            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            _logger.LogInformation("Slug backfill started by user {UserId}. DryRun: {DryRun}", userId, dryRun);

            var recipesWithoutSlugs = await _db.Recipes
                .Where(r => r.Slug == null || r.Slug == "")
                .Include(r => r.User)
                .ToListAsync(ct);

            var updated = 0;
            var errors = new List<string>();

            // Group by user to handle collisions within each user's recipes
            var byUser = recipesWithoutSlugs.GroupBy(r => r.UserId);

            foreach (var userRecipes in byUser)
            {
                var existingSlugs = await _db.Recipes
                    .Where(r => r.UserId == userRecipes.Key && r.Slug != null)
                    .Select(r => r.Slug!)
                    .ToListAsync(ct);

                var slugSet = new HashSet<string>(existingSlugs, StringComparer.OrdinalIgnoreCase);

                foreach (var recipe in userRecipes)
                {
                    try
                    {
                        var baseSlug = Helpers.SlugHelper.GenerateSlug(recipe.Title);
                        var uniqueSlug = Helpers.SlugHelper.MakeUnique(baseSlug, s => slugSet.Contains(s));

                        if (!dryRun)
                        {
                            recipe.Slug = uniqueSlug;
                        }

                        slugSet.Add(uniqueSlug);
                        updated++;

                        _logger.LogDebug("Generated slug for recipe {RecipeId}: {Slug}", recipe.Id, uniqueSlug);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Failed to generate slug for recipe {recipe.Id}: {ex.Message}");
                    }
                }
            }

            if (!dryRun)
            {
                await _db.SaveChangesAsync(ct);

                // Audit log
                await _audit.LogAsync(
                    userId.Value,
                    AdminAction.SystemMigration,
                    "SlugBackfill",
                    null,
                    $"Generated slugs for {updated} recipes",
                    null,
                    new { Updated = updated, Errors = errors.Count },
                    GetIpAddress(),
                    GetUserAgent(),
                    ct);

                _logger.LogInformation("Slug backfill completed. Updated: {Updated}, Errors: {Errors}",
                    updated, errors.Count);
            }

            return Ok(new
            {
                message = dryRun
                    ? $"Dry run completed - would update {updated} recipes"
                    : $"Slug backfill completed - updated {updated} recipes",
                dryRun,
                totalRecipesWithoutSlugs = recipesWithoutSlugs.Count,
                updated,
                errors = errors.Take(50).ToList()
            });
        }
    }

    // Helper classes for SAS migration
    public class SasMigrationStats
    {
        public int UsersWithProfileImages { get; set; }
        public int UsersWithBannerImages { get; set; }
        public int RecipesWithImages { get; set; }
        public int CookbooksWithCovers { get; set; }
        public int TotalRecords { get; set; }
    }

    public class SasMigrationResult
    {
        public int ProfileImagesUpdated { get; set; }
        public int ProfileImagesMissing { get; set; }
        public int ProfileImagesFailed { get; set; }

        public int BannerImagesUpdated { get; set; }
        public int BannerImagesMissing { get; set; }
        public int BannerImagesFailed { get; set; }

        public int RecipeImagesUpdated { get; set; }
        public int RecipeImagesMissing { get; set; }
        public int RecipeImagesFailed { get; set; }

        public int CookbookCoversUpdated { get; set; }
        public int CookbookCoversMissing { get; set; }
        public int CookbookCoversFailed { get; set; }

        public int TotalUpdated => ProfileImagesUpdated + BannerImagesUpdated + RecipeImagesUpdated + CookbookCoversUpdated;
        public int TotalMissing => ProfileImagesMissing + BannerImagesMissing + RecipeImagesMissing + CookbookCoversMissing;
        public int TotalFailed => ProfileImagesFailed + BannerImagesFailed + RecipeImagesFailed + CookbookCoversFailed;

        public List<string> Errors { get; set; } = new();
    }
}
