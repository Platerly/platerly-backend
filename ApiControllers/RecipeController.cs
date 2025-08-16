using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;
using Platerly.Api.Services;
using System.Security.Claims;

namespace Platerly.Api.ApiControllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RecipeController : ControllerBase
    {
        private readonly PlaterlyDbContext _context;
        private readonly IBlobService _blobService;

        public RecipeController(PlaterlyDbContext context, IBlobService blobService)
        {
            _context = context;
            _blobService = blobService;
        }

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

                // Check if global ingredient already exists (case-insensitive match)
                var existingGlobalIngredient = await _context.GlobalIngredients
                    .FirstOrDefaultAsync(g => g.Name.ToLower() == trimmedName);

                if (existingGlobalIngredient == null)
                {
                    existingGlobalIngredient = new GlobalIngredient
                    {
                        Name = i.Name.Trim()
                        // TODO: add default allergen info or flags later
                    };

                    _context.GlobalIngredients.Add(existingGlobalIngredient);
                    await _context.SaveChangesAsync(); // save to get its Id
                }

                // Link to new Ingredient
                var ingredient = new Ingredient
                {
                    Quantity = i.Quantity,
                    GlobalIngredientId = existingGlobalIngredient.Id
                };

                normalizedIngredients.Add(ingredient);
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


        [Authorize]
        [HttpPost("upload-recipe-image")]
        public async Task<IActionResult> UploadRecipeImage(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _context.Users.FindAsync(Guid.Parse(userId));
            if (user == null)
                return NotFound("User not found.");

            var fileName = $"{user.Username}_recipe_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var imageUrl = await _blobService.UploadRecipeImageAsync(file, fileName);

            return Ok(new { imageUrl, imageBlobName = fileName });
        }

        [AllowAnonymous]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetRecipeById(Guid id)
        {
            var recipe = await _context.Recipes
                .Include(r => r.User)
                .Include(r => r.Ingredients)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (recipe == null)
                return NotFound();

            // Prepare basic recipe info
            var recipeDto = new RecipeDto
            {
                Id = recipe.Id,
                Title = recipe.Title,
                Description = recipe.Description,
                Instructions = recipe.Instructions,
                Region = recipe.Region,
                ImageUrl = recipe.ImageUrl,
                CreatedAt = recipe.CreatedAt,
                AuthorUsername = recipe.User?.Username ?? "Unknown", // ✅ this line is key
                Ingredients = recipe.Ingredients.Select(i => new IngredientDto
                {
                    Name = i.Name,
                    Quantity = i.Quantity
                }).ToList(),
                Tags = recipe.Tags,
                PrepTimeMinutes = recipe.PrepTimeMinutes,
                CookTimeMinutes = recipe.CookTimeMinutes,
                Difficulty = recipe.Difficulty,
            };


            // Allergy detection (only if logged in)
            List<string> allergyWarnings = new List<string>();

            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (userId != null)
                {
                    var user = await _context.Users.FindAsync(Guid.Parse(userId));
                    if (user != null && user.AllergyKeywords.Any())
                    {
                        var recipeIngredientNames = recipe.Ingredients.Select(i => i.Name.ToLower()).ToList();

                        foreach (var allergy in user.AllergyKeywords)
                        {
                            var allergyLower = allergy.ToLower();
                            if (recipeIngredientNames.Any(ingredient => ingredient.Contains(allergyLower)))
                            {
                                allergyWarnings.Add(allergy);
                            }
                        }
                    }
                }
            }

            // Final response
            return Ok(new
            {
                recipe = recipeDto,
                allergyWarnings = allergyWarnings
            });
        }


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

            // Check if already reposted
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
        [HttpPost("{originalId}/remix")]
        public async Task<IActionResult> RemixRecipe(Guid originalId, [FromBody] CreateRemixDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var originalRecipe = await _context.Recipes.FindAsync(originalId);
            if (originalRecipe == null)
                return NotFound("Original recipe not found.");

            var remix = new Recipe
            {
                Title = dto.Title,
                Description = dto.Description,
                Instructions = dto.Instructions,
                Region = dto.Region,
                ImageUrl = dto.ImageUrl,
                ImageBlobName = dto.ImageBlobName,
                UserId = Guid.Parse(userId),
                CreatedAt = DateTime.UtcNow,
                Ingredients = dto.Ingredients.Select(i => new Ingredient
                {
                    Quantity = i.Quantity,
                    GlobalIngredient = new GlobalIngredient
                    {
                        Name = i.Name.Trim()
                    }
                }).ToList(),

                Tags = dto.Tags,
                PrepTimeMinutes = dto.PrepTimeMinutes,
                CookTimeMinutes = dto.CookTimeMinutes,
                Difficulty = dto.Difficulty

            };

            _context.Recipes.Add(remix);
            await _context.SaveChangesAsync(); // Save to get remix.Id

            // Step 1: Check if the originalId is already a remix of another recipe
            var existingRemix = await _context.RecipeRemixes
                .FirstOrDefaultAsync(r => r.RemixRecipeId == originalId);

            // Step 2: Determine root origin and immediate parent
            var rootOriginalId = existingRemix?.OriginalRecipeId ?? originalId;
            var parentId = originalId;

            // Step 3: Create the remix link with proper ancestry
            var remixLink = new RecipeRemix
            {
                RemixRecipeId = remix.Id,
                OriginalRecipeId = rootOriginalId,  // origin/root of the chain
                ParentRecipeId = parentId,          // direct parent in the chain
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

                if (recipe == null)
                    break;

                // Add to the beginning of the list (so it's in order from original → current)
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

                // Check if this recipe is a remix
                var remixRecord = await _context.RecipeRemixes
                    .FirstOrDefaultAsync(r => r.RemixRecipeId == currentRecipeId);

                if (remixRecord == null)
                    break;

                // Move to the parent
                if (remixRecord.OriginalRecipeId == null)
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
                .Where(r => r.OriginalRecipeId == id)
                .Include(r => r.RemixRecipe)
                    .ThenInclude(rr => rr.Ingredients)
                .Select(r => new RecipeDto
                {
                    Id = r.RemixRecipe.Id,
                    Title = r.RemixRecipe.Title,
                    Description = r.RemixRecipe.Description,
                    Instructions = r.RemixRecipe.Instructions,
                    Region = r.RemixRecipe.Region,
                    ImageUrl = r.RemixRecipe.ImageUrl,
                    CreatedAt = r.RemixRecipe.CreatedAt,
                    Ingredients = r.RemixRecipe.Ingredients.Select(i => new IngredientDto
                    {
                        Name = i.Name,
                        Quantity = i.Quantity
                    }).ToList(),
                    Tags = r.RemixRecipe.Tags,
                    PrepTimeMinutes = r.RemixRecipe.PrepTimeMinutes,
                    CookTimeMinutes = r.RemixRecipe.CookTimeMinutes,
                    Difficulty = r.RemixRecipe.Difficulty,
                })
                .ToListAsync();

            return Ok(remixes);
        }






    }
}
