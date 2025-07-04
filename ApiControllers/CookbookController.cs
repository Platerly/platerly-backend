using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;
using System.Security.Claims;

namespace Platerly.Api.ApiControllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CookbookController : ControllerBase
    {
        private readonly PlaterlyDbContext _context;

        public CookbookController(PlaterlyDbContext context)
        {
            _context = context;
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> CreateCookbook([FromBody] CreateCookbookDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var cookbook = new Cookbook
            {
                Title = dto.Title,
                IsPublic = dto.IsPublic,
                UserId = Guid.Parse(userId),
                CreatedAt = DateTime.UtcNow
            };

            _context.Cookbooks.Add(cookbook);
            await _context.SaveChangesAsync();

            return Ok(new { cookbook.Id });
        }

        [Authorize]
        [HttpPost("{cookbookId}/add-recipe")]
        public async Task<IActionResult> AddRecipeToCookbook(Guid cookbookId, [FromBody] AddRecipeToCookbookDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var cookbook = await _context.Cookbooks.FindAsync(cookbookId);
            if (cookbook == null || cookbook.UserId != Guid.Parse(userId))
                return NotFound("Cookbook not found or access denied.");

            var recipe = await _context.Recipes.FindAsync(dto.RecipeId);
            if (recipe == null)
                return NotFound("Recipe not found.");

            var alreadyExists = await _context.CookbookRecipes
                .AnyAsync(cr => cr.CookbookId == cookbookId && cr.RecipeId == dto.RecipeId);

            if (alreadyExists)
                return BadRequest("Recipe already in cookbook.");

            var cookbookRecipe = new CookbookRecipe
            {
                CookbookId = cookbookId,
                RecipeId = dto.RecipeId
            };

            _context.CookbookRecipes.Add(cookbookRecipe);
            await _context.SaveChangesAsync();

            return Ok("Recipe added to cookbook.");
        }

        [Authorize]
        [HttpGet("my-cookbooks")]
        public async Task<IActionResult> GetMyCookbooks()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var cookbooks = await _context.Cookbooks
                .Where(c => c.UserId == Guid.Parse(userId))
                .Select(c => new CookbookDto
                {
                    Id = c.Id,
                    Title = c.Title,
                    IsPublic = c.IsPublic,
                    CreatedAt = c.CreatedAt,
                    Recipes = _context.CookbookRecipes
                        .Where(cr => cr.CookbookId == c.Id)
                        .Include(cr => cr.Recipe)
                            .ThenInclude(r => r.Ingredients)
                        .Select(cr => new RecipeDto
                        {
                            Id = cr.Recipe.Id,
                            Title = cr.Recipe.Title,
                            Description = cr.Recipe.Description,
                            Instructions = cr.Recipe.Instructions,
                            Region = cr.Recipe.Region,
                            ImageUrl = cr.Recipe.ImageUrl,
                            CreatedAt = cr.Recipe.CreatedAt,
                            Ingredients = cr.Recipe.Ingredients.Select(i => new IngredientDto
                            {
                                Name = i.Name,
                                Quantity = i.Quantity
                            }).ToList()
                        }).ToList()
                })
                .ToListAsync();

            return Ok(cookbooks);
        }

        [AllowAnonymous]
        [HttpGet("public")]
        public async Task<IActionResult> GetPublicCookbooks()
        {
            var cookbooks = await _context.Cookbooks
                .Where(c => c.IsPublic)
                .Select(c => new CookbookDto
                {
                    Id = c.Id,
                    Title = c.Title,
                    IsPublic = c.IsPublic,
                    CreatedAt = c.CreatedAt,
                    Recipes = _context.CookbookRecipes
                        .Where(cr => cr.CookbookId == c.Id)
                        .Include(cr => cr.Recipe)
                            .ThenInclude(r => r.Ingredients)
                        .Select(cr => new RecipeDto
                        {
                            Id = cr.Recipe.Id,
                            Title = cr.Recipe.Title,
                            Description = cr.Recipe.Description,
                            Instructions = cr.Recipe.Instructions,
                            Region = cr.Recipe.Region,
                            ImageUrl = cr.Recipe.ImageUrl,
                            CreatedAt = cr.Recipe.CreatedAt,
                            Ingredients = cr.Recipe.Ingredients.Select(i => new IngredientDto
                            {
                                Name = i.Name,
                                Quantity = i.Quantity
                            }).ToList()
                        }).ToList()
                })
                .ToListAsync();

            return Ok(cookbooks);
        }

        [AllowAnonymous]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetCookbookById(Guid id)
        {
            var cookbook = await _context.Cookbooks
                .Where(c => c.Id == id)
                .Select(c => new CookbookDto
                {
                    Id = c.Id,
                    Title = c.Title,
                    IsPublic = c.IsPublic,
                    CreatedAt = c.CreatedAt,
                    Recipes = _context.CookbookRecipes
                        .Where(cr => cr.CookbookId == c.Id)
                        .Include(cr => cr.Recipe)
                            .ThenInclude(r => r.Ingredients)
                        .Select(cr => new RecipeDto
                        {
                            Id = cr.Recipe.Id,
                            Title = cr.Recipe.Title,
                            Description = cr.Recipe.Description,
                            Instructions = cr.Recipe.Instructions,
                            Region = cr.Recipe.Region,
                            ImageUrl = cr.Recipe.ImageUrl,
                            CreatedAt = cr.Recipe.CreatedAt,
                            Ingredients = cr.Recipe.Ingredients.Select(i => new IngredientDto
                            {
                                Name = i.Name,
                                Quantity = i.Quantity
                            }).ToList()
                        }).ToList()
                })
                .FirstOrDefaultAsync();

            if (cookbook == null)
                return NotFound();

            return Ok(cookbook);
        }

        [Authorize]
        [HttpDelete("{cookbookId}/remove-recipe/{recipeId}")]
        public async Task<IActionResult> RemoveRecipeFromCookbook(Guid cookbookId, Guid recipeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var cookbook = await _context.Cookbooks
                .FirstOrDefaultAsync(c => c.Id == cookbookId && c.UserId == Guid.Parse(userId));

            if (cookbook == null)
                return NotFound("Cookbook not found or access denied.");

            var entry = await _context.CookbookRecipes
                .FirstOrDefaultAsync(cr => cr.CookbookId == cookbookId && cr.RecipeId == recipeId);

            if (entry == null)
                return NotFound("Recipe not found in this cookbook.");

            _context.CookbookRecipes.Remove(entry);
            await _context.SaveChangesAsync();

            return NoContent();
        }

    }
}
