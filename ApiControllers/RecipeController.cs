using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;
using Platerly.Api.Services;
using System.Security.Claims;
using System.IO;

namespace Platerly.Api.ApiControllers
{
    // -------- Existing multipart/form-data DTO (kept) --------
    public class UpdateRecipeDto
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Instructions { get; set; }
        public string? Region { get; set; }
        public string? Tags { get; set; }
        public int? PrepTimeMinutes { get; set; }
        public int? CookTimeMinutes { get; set; }
        public string? Difficulty { get; set; }
        public IFormFile? Image { get; set; } // optional image replacement
    }



    // -------- NEW: clean JSON DTOs for text-only edits --------
    public class IngredientUpsertDto
    {
        public string Name { get; set; } = "";
        public string Quantity { get; set; } = "";
    }

    public class UpdateRecipeJsonDto
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Instructions { get; set; }
        public string? Region { get; set; }
        public string? Tags { get; set; }
        public int? PrepTimeMinutes { get; set; }
        public int? CookTimeMinutes { get; set; }
        public string? Difficulty { get; set; }
        // Optional: replace full ingredient list
        public List<IngredientUpsertDto>? Ingredients { get; set; }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class RecipeController : ControllerBase
    {
        private readonly PlaterlyDbContext _context;
        private readonly IBlobService _blobService;
        private readonly ILogger<RecipeController> _log;

        public RecipeController(PlaterlyDbContext context, IBlobService blobService, ILogger<RecipeController> log)
        {
            _context = context;
            _blobService = blobService;
            _log = log;
        }

        private Guid? CurrentUserId()
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(id, out var g) ? g : null;
        }

        // ---------------------- Create ----------------------

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> CreateRecipe([FromBody] CreateRecipeDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var normalizedIngredients = new List<Ingredient>();

            foreach (var i in dto.Ingredients)
            {
                var trimmedName = i.Name.Trim().ToLower();

                var existingGlobalIngredient = await _context.GlobalIngredients
                    .FirstOrDefaultAsync(g => g.Name.ToLower() == trimmedName);

                if (existingGlobalIngredient == null)
                {
                    existingGlobalIngredient = new GlobalIngredient { Name = i.Name.Trim() };
                    _context.GlobalIngredients.Add(existingGlobalIngredient);
                    await _context.SaveChangesAsync(); // get Id
                }

                normalizedIngredients.Add(new Ingredient
                {
                    Quantity = i.Quantity,
                    GlobalIngredientId = existingGlobalIngredient.Id
                });
            }

            var recipe = new Recipe
            {
                Title = dto.Title,
                Description = dto.Description,
                Instructions = dto.Instructions,
                Region = dto.Region,
                ImageUrl = dto.ImageUrl,
                ImageBlobName = dto.ImageBlobName,
                UserId = Guid.Parse(userId),
                CreatedAt = DateTime.UtcNow,
                Tags = string.Join(",", dto.Tags),
                PrepTimeMinutes = dto.PrepTimeMinutes,
                CookTimeMinutes = dto.CookTimeMinutes,
                Difficulty = dto.Difficulty,
                Ingredients = normalizedIngredients
            };

            _context.Recipes.Add(recipe);
            await _context.SaveChangesAsync();

            var result = new RecipeDto
            {
                Id = recipe.Id,
                Title = recipe.Title,
                Description = recipe.Description,
                Instructions = recipe.Instructions,
                Region = recipe.Region,
                ImageUrl = recipe.ImageUrl,
                CreatedAt = recipe.CreatedAt,
                Ingredients = recipe.Ingredients.Select(i => new IngredientDto
                {
                    Name = i.GlobalIngredient?.Name ?? "Unknown",
                    Quantity = i.Quantity
                }).ToList(),
                Tags = recipe.Tags,
                PrepTimeMinutes = recipe.PrepTimeMinutes,
                CookTimeMinutes = recipe.CookTimeMinutes,
                Difficulty = recipe.Difficulty,
            };

            return Ok(result);
        }

        // Upload one-off image (frontend helper)
        [Authorize]
        [HttpPost("upload-recipe-image")]
        public async Task<IActionResult> UploadRecipeImage(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _context.Users.FindAsync(Guid.Parse(userId!));
            if (user == null) return NotFound("User not found.");

            var fileName = $"{user.Username}_recipe_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var imageUrl = await _blobService.UploadRecipeImageAsync(file, fileName);

            return Ok(new { imageUrl, imageBlobName = fileName });
        }

        // ---------------------- Read ----------------------

        [AllowAnonymous]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetRecipeById(Guid id)
        {
            var recipe = await _context.Recipes
                .Include(r => r.User)
                .Include(r => r.Ingredients).ThenInclude(i => i.GlobalIngredient)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (recipe == null)
                return NotFound();

            var recipeDto = new RecipeDto
            {
                Id = recipe.Id,
                Title = recipe.Title,
                Description = recipe.Description,
                Instructions = recipe.Instructions,
                Region = recipe.Region,
                ImageUrl = recipe.ImageUrl,
                CreatedAt = recipe.CreatedAt,
                AuthorUsername = recipe.User?.Username ?? "Unknown",
                Ingredients = recipe.Ingredients.Select(i => new IngredientDto
                {
                    Name = i.GlobalIngredient != null ? i.GlobalIngredient.Name : i.Name,
                    Quantity = i.Quantity
                }).ToList(),
                Tags = recipe.Tags,
                PrepTimeMinutes = recipe.PrepTimeMinutes,
                CookTimeMinutes = recipe.CookTimeMinutes,
                Difficulty = recipe.Difficulty,
            };

            // (Optional) allergy warnings for logged in users
            List<string> allergyWarnings = new();
            if (User.Identity?.IsAuthenticated == true)
            {
                var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (Guid.TryParse(uid, out var g))
                {
                    var viewer = await _context.Users.FindAsync(g);
                    if (viewer != null && viewer.AllergyKeywords.Any())
                    {
                        var names = recipe.Ingredients
                            .Select(i => (i.GlobalIngredient?.Name ?? i.Name ?? "").ToLower())
                            .ToList();

                        foreach (var allergy in viewer.AllergyKeywords)
                        {
                            var a = allergy.ToLower();
                            if (names.Any(n => n.Contains(a))) allergyWarnings.Add(allergy);
                        }
                    }
                }
            }

            
            var remixLink = await _context.RecipeRemixes
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.RemixRecipeId == id);

            return Ok(new
            {
                recipe = recipeDto,
                allergyWarnings,
                remixMeta = remixLink == null
                    ? null
                    : new
                    {
                        isRemix = true,
                        parentRecipeId = remixLink.ParentRecipeId,
                        originalRecipeId = remixLink.OriginalRecipeId
                    }
            });
        }


        // ---------------------- Update ----------------------

        // A) Multipart/form-data update (with optional image)
        [Authorize]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromForm] UpdateRecipeDto dto)
        {
            var userId = CurrentUserId();
            if (userId is null) return Unauthorized();

            var recipe = await _context.Recipes
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.Id == id);

            if (recipe is null) return NotFound();
            if (recipe.UserId != userId) return Forbid();
            if (recipe.IsDeleted) return StatusCode(StatusCodes.Status410Gone, "This recipe is unavailable.");

            // Partial updates
            if (dto.Title != null) recipe.Title = dto.Title;
            if (dto.Description != null) recipe.Description = dto.Description;
            if (dto.Instructions != null) recipe.Instructions = dto.Instructions;
            if (dto.Region != null) recipe.Region = dto.Region;
            if (dto.Tags != null) recipe.Tags = dto.Tags;
            if (dto.PrepTimeMinutes.HasValue) recipe.PrepTimeMinutes = dto.PrepTimeMinutes.Value;
            if (dto.CookTimeMinutes.HasValue) recipe.CookTimeMinutes = dto.CookTimeMinutes.Value;
            if (dto.Difficulty != null) recipe.Difficulty = dto.Difficulty;

            string? oldBlob = recipe.ImageBlobName;

            if (dto.Image is not null && dto.Image.Length > 0)
            {
                var newBlob = $"recipe_{recipe.Id}_{Guid.NewGuid()}{Path.GetExtension(dto.Image.FileName)}";
                string newUrl;

                try
                {
                    newUrl = await _blobService.UploadRecipeImageAsync(dto.Image, newBlob);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Recipe image upload failed for recipe {RecipeId}", recipe.Id);
                    return StatusCode(StatusCodes.Status500InternalServerError, "Image upload failed.");
                }

                recipe.ImageBlobName = newBlob;
                recipe.ImageUrl = newUrl;
            }

            recipe.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict("This recipe was updated by someone else. Please refresh and try again.");
            }

            // Best-effort cleanup AFTER successful save
            if (!string.IsNullOrWhiteSpace(oldBlob) && oldBlob != recipe.ImageBlobName)
            {
                try { await _blobService.DeleteRecipeImageIfExistsAsync(oldBlob); }
                catch (Exception ex) { _log.LogWarning(ex, "Failed deleting old recipe blob {Blob}", oldBlob); }
            }

            return Ok(new
            {
                recipe.Id,
                recipe.Title,
                recipe.Description,
                recipe.Instructions,
                recipe.Region,
                recipe.Tags,
                recipe.PrepTimeMinutes,
                recipe.CookTimeMinutes,
                recipe.Difficulty,
                recipe.ImageUrl,
                recipe.UpdatedAt
            });
        }

        // B) NEW: JSON update (text-only edits; optional full ingredient replace)
        [Authorize]
        [HttpPut("{id:guid}/json")]
        [Consumes("application/json")]
        public async Task<IActionResult> UpdateJson(Guid id, [FromBody] UpdateRecipeJsonDto dto)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

            var recipe = await _context.Recipes
                .IgnoreQueryFilters()
                .Include(r => r.Ingredients)
                    .ThenInclude(i => i.GlobalIngredient)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (recipe is null) return NotFound();
            if (recipe.UserId != userId) return Forbid();
            if (recipe.IsDeleted) return StatusCode(StatusCodes.Status410Gone, "This recipe is unavailable.");

            // Patch scalar fields
            if (dto.Title != null) recipe.Title = dto.Title;
            if (dto.Description != null) recipe.Description = dto.Description;
            if (dto.Instructions != null) recipe.Instructions = dto.Instructions;
            if (dto.Region != null) recipe.Region = dto.Region;
            if (dto.Tags != null) recipe.Tags = dto.Tags;
            if (dto.PrepTimeMinutes.HasValue) recipe.PrepTimeMinutes = dto.PrepTimeMinutes.Value;
            if (dto.CookTimeMinutes.HasValue) recipe.CookTimeMinutes = dto.CookTimeMinutes.Value;
            if (dto.Difficulty != null) recipe.Difficulty = dto.Difficulty;

            // Optional: replace full ingredient list (normalized to GlobalIngredient)
            if (dto.Ingredients != null)
            {
                recipe.Ingredients.Clear();

                foreach (var i in dto.Ingredients)
                {
                    var trimmed = (i.Name ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;

                    var lower = trimmed.ToLower();
                    var global = await _context.GlobalIngredients
                        .FirstOrDefaultAsync(g => g.Name.ToLower() == lower);

                    if (global == null)
                    {
                        global = new GlobalIngredient { Name = trimmed };
                        _context.GlobalIngredients.Add(global);
                        await _context.SaveChangesAsync(); // ensure Id
                    }

                    recipe.Ingredients.Add(new Ingredient
                    {
                        Quantity = i.Quantity ?? "",
                        GlobalIngredientId = global.Id
                    });
                }
            }

            recipe.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                recipe.Id,
                recipe.Title,
                recipe.Description,
                recipe.Instructions,
                recipe.Region,
                recipe.Tags,
                recipe.PrepTimeMinutes,
                recipe.CookTimeMinutes,
                recipe.Difficulty,
                recipe.ImageUrl,
                recipe.UpdatedAt
            });
        }

        // ---------------------- Delete (soft) ----------------------

        [Authorize]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> SoftDelete(Guid id)
        {
            var userId = CurrentUserId();
            if (userId is null) return Unauthorized();

            var recipe = await _context.Recipes
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.Id == id);

            if (recipe is null) return NotFound();
            if (recipe.UserId != userId) return Forbid();

            if (recipe.IsDeleted) return NoContent(); // already hidden

            recipe.IsDeleted = true;
            recipe.DeletedAt = DateTime.UtcNow;
            recipe.DeletedByUserId = userId;
            recipe.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // ---------------------- Likes / Comments / Remix / Remixes ----------------------

        [Authorize]
        [HttpPost("{id}/like")]
        public async Task<IActionResult> LikeRecipe(Guid id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var recipe = await _context.Recipes.FindAsync(id);
            if (recipe == null) return NotFound("Recipe not found.");

            var alreadyLiked = await _context.RecipeLikes
                .AnyAsync(rl => rl.RecipeId == id && rl.UserId == Guid.Parse(userId));

            if (alreadyLiked)
                return BadRequest("You already liked this recipe.");

            var like = new RecipeLike
            {
                RecipeId = id,
                UserId = Guid.Parse(userId),
                LikedAt = DateTime.UtcNow
            };

            _context.RecipeLikes.Add(like);
            await _context.SaveChangesAsync();

            return Ok("Recipe liked.");
        }

        [Authorize]
        [HttpDelete("{id}/like")]
        public async Task<IActionResult> UnlikeRecipe(Guid id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var like = await _context.RecipeLikes
                .FirstOrDefaultAsync(rl => rl.RecipeId == id && rl.UserId == Guid.Parse(userId));

            if (like == null)
                return NotFound("You haven't liked this recipe.");

            _context.RecipeLikes.Remove(like);
            await _context.SaveChangesAsync();

            return Ok("Recipe unliked.");
        }

        [Authorize]
        [HttpPost("{recipeId}/comments")]
        public async Task<IActionResult> AddComment(Guid recipeId, [FromBody] CreateCommentDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var recipe = await _context.Recipes.FindAsync(recipeId);
            if (recipe == null) return NotFound("Recipe not found.");

            var comment = new Comment
            {
                Content = dto.Content,
                CreatedAt = DateTime.UtcNow,
                RecipeId = recipeId,
                UserId = Guid.Parse(userId)
            };

            _context.Comments.Add(comment);
            await _context.SaveChangesAsync();

            return Ok(new { comment.Id, comment.Content, comment.CreatedAt });
        }

        [AllowAnonymous]
        [HttpGet("{recipeId}/comments")]
        public async Task<ActionResult<IEnumerable<CommentDto>>> GetComments(Guid recipeId)
        {
            var comments = await _context.Comments
                .Where(c => c.RecipeId == recipeId)
                .Include(c => c.User)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new CommentDto
                {
                    Id = c.Id,
                    Content = c.Content,
                    CreatedAt = c.CreatedAt,
                    UserId = c.UserId,
                    Username = c.User!.Username,
                    DisplayName = c.User.DisplayName,
                    ProfileImageUrl = c.User.ProfileImageUrl
                })
                .ToListAsync();

            return Ok(comments);
        }

        [Authorize]
        [HttpPost("{id}/repost")]
        public async Task<IActionResult> RepostRecipe(Guid id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var existingRecipe = await _context.Recipes.FindAsync(id);
            if (existingRecipe == null)
                return NotFound("Recipe not found.");

            var userGuid = Guid.Parse(userId);

            var alreadyReposted = await _context.RecipeReposts
                .AnyAsync(r => r.UserId == userGuid && r.RecipeId == id);

            if (alreadyReposted)
                return BadRequest("You have already reposted this recipe.");

            var repost = new RecipeRepost
            {
                UserId = userGuid,
                RecipeId = id,
                RepostedAt = DateTime.UtcNow
            };

            _context.RecipeReposts.Add(repost);
            await _context.SaveChangesAsync();

            return Ok("Recipe reposted successfully.");
        }

        [Authorize]
        [HttpDelete("{id}/repost")]
        public async Task<IActionResult> UnrepostRecipe(Guid id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var userGuid = Guid.Parse(userId);

            var repost = await _context.RecipeReposts
                .FirstOrDefaultAsync(r => r.UserId == userGuid && r.RecipeId == id);

            if (repost == null)
                return NotFound("You have not reposted this recipe.");

            _context.RecipeReposts.Remove(repost);
            await _context.SaveChangesAsync();

            return Ok("Repost removed successfully.");
        }

        [Authorize]
        [HttpPost("{originalId:guid}/remix")]
        public async Task<IActionResult> RemixRecipe(Guid originalId, [FromBody] CreateRecipeDto dto)
        {
            // Helpful diagnostics if binding fails
            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .Where(kvp => kvp.Value?.Errors?.Count > 0)
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                    );
                return BadRequest(new { message = "Invalid remix payload", errors });
            }

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

            // Ensure original exists (respect soft delete filter)
            var originalRecipe = await _context.Recipes.FirstOrDefaultAsync(r => r.Id == originalId);
            if (originalRecipe == null) return NotFound("Original recipe not found.");

            // --- Normalize ingredients (same as Create) ---
            var normalizedIngredients = new List<Ingredient>();

            foreach (var i in dto.Ingredients)
            {
                var trimmedNameLower = i.Name.Trim().ToLower();

                var existingGlobalIngredient = await _context.GlobalIngredients
                    .FirstOrDefaultAsync(g => g.Name.ToLower() == trimmedNameLower);

                if (existingGlobalIngredient == null)
                {
                    existingGlobalIngredient = new GlobalIngredient { Name = i.Name.Trim() };
                    _context.GlobalIngredients.Add(existingGlobalIngredient);
                    await _context.SaveChangesAsync(); // get Id
                }

                normalizedIngredients.Add(new Ingredient
                {
                    Quantity = i.Quantity,
                    GlobalIngredientId = existingGlobalIngredient.Id
                });
            }

            // --- Normalize tags --- 
            // In your app, CreateRecipeDto.Tags is a List<string> (not string).
            // Safely join into a single comma-separated string.
            string tagsJoined;
            if (dto.Tags != null && dto.Tags.Count > 0)
            {
                tagsJoined = string.Join(",", dto.Tags
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim()));
            }
            else
            {
                tagsJoined = string.Empty;
            }


            var remix = new Recipe
            {
                Title = dto.Title,
                Description = dto.Description,
                Instructions = dto.Instructions,
                Region = dto.Region,
                ImageUrl = dto.ImageUrl,          // optional if your Remix page uploads separately
                ImageBlobName = dto.ImageBlobName,
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                Ingredients = normalizedIngredients,
                Tags = tagsJoined,
                PrepTimeMinutes = dto.PrepTimeMinutes,
                CookTimeMinutes = dto.CookTimeMinutes,
                Difficulty = dto.Difficulty
            };

            _context.Recipes.Add(remix);
            await _context.SaveChangesAsync(); // get remix.Id

            // If the original was itself a remix, preserve lineage (root + parent)
            var existingRemix = await _context.RecipeRemixes
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.RemixRecipeId == originalId);

            var rootOriginalId = existingRemix?.OriginalRecipeId ?? originalId;
            var parentId = originalId;

            var remixLink = new RecipeRemix
            {
                RemixRecipeId = remix.Id,
                OriginalRecipeId = rootOriginalId,
                ParentRecipeId = parentId,
                RemixedAt = DateTime.UtcNow
            };

            _context.RecipeRemixes.Add(remixLink);
            await _context.SaveChangesAsync();

            return Ok(new { remixId = remix.Id });
        }


        [AllowAnonymous]
        [HttpGet("{id}/lineage")]
        public async Task<IActionResult> GetRemixLineage(Guid id)
        {
            var lineage = new List<RecipeDto>();
            var currentRecipeId = id;

            while (true)
            {
                var recipe = await _context.Recipes
                    .Include(r => r.Ingredients)
                    .Include(r => r.User)
                    .FirstOrDefaultAsync(r => r.Id == currentRecipeId);

                if (recipe == null) break;

                lineage.Insert(0, new RecipeDto
                {
                    Id = recipe.Id,
                    Title = recipe.Title,
                    Description = recipe.Description,
                    Instructions = recipe.Instructions,
                    Region = recipe.Region,
                    ImageUrl = recipe.ImageUrl,
                    CreatedAt = recipe.CreatedAt,
                    Ingredients = recipe.Ingredients.Select(i => new IngredientDto
                    {
                        Name = i.Name,
                        Quantity = i.Quantity
                    }).ToList(),
                    Tags = recipe.Tags,
                    PrepTimeMinutes = recipe.PrepTimeMinutes,
                    CookTimeMinutes = recipe.CookTimeMinutes,
                    Difficulty = recipe.Difficulty,
                });

                var remixRecord = await _context.RecipeRemixes
                    .FirstOrDefaultAsync(r => r.RemixRecipeId == currentRecipeId);

                if (remixRecord == null || remixRecord.OriginalRecipeId == null)
                    break;

                currentRecipeId = remixRecord.OriginalRecipeId.Value;
            }

            return Ok(lineage);
        }

        [AllowAnonymous]
        [HttpGet("{id}/remixes")]
        public async Task<IActionResult> GetRemixes(Guid id)
        {
            var remixes = await _context.RecipeRemixes
                // show all remixes that descend from this original,
                // OR direct children if you prefer just `.Where(r => r.ParentRecipeId == id)`
                .Where(r => r.OriginalRecipeId == id || r.ParentRecipeId == id)
                .Include(r => r.RemixRecipe)
                    .ThenInclude(rr => rr.User)
                .Include(r => r.RemixRecipe)
                    .ThenInclude(rr => rr.Ingredients)
                        .ThenInclude(i => i.GlobalIngredient)
                .AsNoTracking()
                .Select(r => new RemixDto
                {
                    Id = r.RemixRecipe.Id,
                    Title = r.RemixRecipe.Title,
                    Description = r.RemixRecipe.Description,
                    Instructions = r.RemixRecipe.Instructions,
                    Region = r.RemixRecipe.Region,
                    ImageUrl = r.RemixRecipe.ImageUrl,
                    CreatedAt = r.RemixRecipe.CreatedAt,

                    UserId = r.RemixRecipe.UserId,
                    Username = r.RemixRecipe.User.Username,
                    DisplayName = r.RemixRecipe.User.DisplayName,
                    ProfileImageUrl = r.RemixRecipe.User.ProfileImageUrl,

                    Tags = r.RemixRecipe.Tags,
                    PrepTimeMinutes = r.RemixRecipe.PrepTimeMinutes,
                    CookTimeMinutes = r.RemixRecipe.CookTimeMinutes,
                    Difficulty = r.RemixRecipe.Difficulty,

                    Ingredients = r.RemixRecipe.Ingredients.Select(i => new IngredientDto
                    {
                        Name = i.GlobalIngredient != null ? i.GlobalIngredient.Name : i.Name,
                        Quantity = i.Quantity
                    }).ToList(),

                    
                    ParentRecipeId = r.ParentRecipeId,
                    OriginalRecipeId = r.OriginalRecipeId
                })
                .ToListAsync();

            return Ok(remixes);
        }

    }
}
