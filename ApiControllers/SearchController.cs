using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using System.Linq;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// Public B2C search controller that provides paginated full-text search across
    /// both users and recipes in a single request.
    /// Base route: <c>api/search</c>.
    /// Anonymous access is allowed; rate-limited via the <c>anonymous</c> policy.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class SearchController : ControllerBase
    {
        private readonly PlaterlyDbContext _context;

        public SearchController(PlaterlyDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Search for users and recipes whose username/display name or title/description
        /// contains the given query string. Both result sets are paginated independently
        /// using the same <paramref name="page"/> and <paramref name="pageSize"/> values.
        /// Empty queries return empty arrays immediately without hitting the database.
        /// </summary>
        /// <param name="q">Search term (max 100 characters). Case-insensitive substring match.</param>
        /// <param name="page">1-based page number (default 1).</param>
        /// <param name="pageSize">Results per page, clamped 1–50 (default 20).</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ users, recipes, totalUsers, totalRecipes, page, pageSize,
        /// hasMoreUsers, hasMoreRecipes }</c>. User objects include type/username/displayName/bio/
        /// profileImageUrl. Recipe objects include type/recipeId/title/description/imageUrl/likes/
        /// authorUsername/ingredients.<br/>
        /// <c>400</c> — query exceeds 100 characters.<br/>
        /// <c>429</c> — anonymous rate limit exceeded.
        /// </returns>
        [HttpGet]
        [AllowAnonymous]
        [EnableRateLimiting("anonymous")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        public async Task<IActionResult> Search(
            [FromQuery] string q,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            if (string.IsNullOrWhiteSpace(q))
                return Ok(new { users = Array.Empty<object>(), recipes = Array.Empty<object>(), totalUsers = 0, totalRecipes = 0 });

            // Input validation
            if (q.Length > 100)
                return BadRequest("Search query too long (max 100 characters)");

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var searchTerm = q.ToLower();
            var skip = (page - 1) * pageSize;

            // Get total counts for pagination info
            var totalUsers = await _context.Users
                .CountAsync(u => u.Username.ToLower().Contains(searchTerm) || u.DisplayName.ToLower().Contains(searchTerm));

            var totalRecipes = await _context.Recipes
                .CountAsync(r => r.Title.ToLower().Contains(searchTerm) || r.Description.ToLower().Contains(searchTerm));

            var users = await _context.Users
                .Where(u => u.Username.ToLower().Contains(searchTerm) || u.DisplayName.ToLower().Contains(searchTerm))
                .OrderBy(u => u.Username)
                .Skip(skip)
                .Take(pageSize)
                .Select(u => new
                {
                    type = "user",
                    username = u.Username,
                    displayName = u.DisplayName,
                    bio = u.Bio,
                    profileImageUrl = u.ProfileImageUrl
                })
                .ToListAsync();

            var recipes = await _context.Recipes
                .Where(r => r.Title.ToLower().Contains(searchTerm) || r.Description.ToLower().Contains(searchTerm))
                .OrderByDescending(r => r.CreatedAt)
                .Skip(skip)
                .Take(pageSize)
                .Select(r => new
                {
                    type = "recipe",
                    recipeId = r.Id,
                    title = r.Title,
                    description = r.Description,
                    imageUrl = r.ImageUrl,
                    likes = r.Likes,
                    authorUsername = r.User.Username,
                    ingredients = r.Ingredients.Select(i => new { i.Name, i.Quantity })
                })
                .ToListAsync();

            return Ok(new
            {
                users,
                recipes,
                totalUsers,
                totalRecipes,
                page,
                pageSize,
                hasMoreUsers = skip + users.Count < totalUsers,
                hasMoreRecipes = skip + recipes.Count < totalRecipes
            });
        }


    }
}
