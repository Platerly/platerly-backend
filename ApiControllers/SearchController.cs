using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using System.Linq;

namespace Platerly.Api.ApiControllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SearchController : ControllerBase
    {
        private readonly PlaterlyDbContext _context;

        public SearchController(PlaterlyDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Search([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return Ok(new List<object>()); // return an empty array instead of an object

            q = q.ToLower();

            var users = await _context.Users
                .Where(u => u.Username.ToLower().Contains(q) || u.DisplayName.ToLower().Contains(q))
                .Select(u => new
                {
                    u.Username,
                    u.DisplayName,
                    u.Bio,
                    u.ProfileImageUrl
                })
                .ToListAsync();

            var recipes = await _context.Recipes
                .Where(r => r.Title.ToLower().Contains(q) || r.Description.ToLower().Contains(q))
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new
                {
                    recipeId = r.Id,
                    r.Title,
                    r.Description,
                    r.ImageUrl,
                    r.Likes,
                    AuthorUsername = r.User.Username,
                    Ingredients = r.Ingredients.Select(i => new { i.Name, i.Quantity })
                })
                .ToListAsync();

            return Ok(users.Select(u => (object)new
            {
                type = "user",
                username = u.Username,
                displayName = u.DisplayName,
                bio = u.Bio,
                profileImageUrl = u.ProfileImageUrl
            }).Concat(recipes.Select(r => (object)new
        {
            type = "recipe",
            recipeId = r.recipeId,
            title = r.Title,
            description = r.Description,
            imageUrl = r.ImageUrl,
            likes = r.Likes,
            authorUsername = r.AuthorUsername,
            ingredients = r.Ingredients
        })
    ).ToList()
);

        }


    }
}
