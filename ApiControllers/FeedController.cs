using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using System.Security.Claims;

namespace Platerly.Api.ApiControllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FeedController : ControllerBase
    {
        private readonly PlaterlyDbContext _context;

        public FeedController(PlaterlyDbContext context)
        {
            _context = context;
        }

        [AllowAnonymous]
        [HttpGet("explore")]
        public async Task<IActionResult> GetExploreFeed()
        {
            Guid? currentUserId = null;
            var followingIds = new List<Guid>();

            if (User.Identity?.IsAuthenticated == true)
            {
                var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (Guid.TryParse(userIdString, out Guid parsedId))
                {
                    currentUserId = parsedId;
                }
            }

            if (currentUserId.HasValue)
            {
                followingIds = await _context.UserFollows
                    .Where(f => f.FollowerId == currentUserId.Value)
                    .Select(f => f.FollowingId)
                    .ToListAsync();
            }

            var allRemixes = await _context.RecipeRemixes
                .Include(r => r.RemixRecipe)
                .Where(r => r.OriginalRecipeId != null)
                .ToListAsync();

            var remixIds = allRemixes.Select(r => r.RemixRecipeId).ToList();

            var remixCountDict = allRemixes
                .GroupBy(r => r.OriginalRecipeId)
                .ToDictionary(g => g.Key!.Value, g => g.Count());

            var userRemixedOriginals = currentUserId.HasValue
                ? allRemixes
                    .Where(r => r.RemixRecipe.UserId == currentUserId.Value)
                    .Select(r => r.OriginalRecipeId!.Value)
                    .ToList()
                : new List<Guid>();

            var repostedIds = currentUserId.HasValue
                ? await _context.RecipeReposts
                    .Where(r => r.UserId == currentUserId.Value)
                    .Select(r => r.RecipeId)
                    .ToListAsync()
                : new List<Guid>();

            var publicRecipes = await _context.Recipes
                .Where(r => r.User != null)
                .OrderByDescending(r => r.CreatedAt)
                .Include(r => r.Ingredients)
                .Include(r => r.User)
                .Include(r => r.Likes)
                .Include(r => r.Reposts)
                .Take(30)
                .ToListAsync();

            var feed = publicRecipes.Select(r => new RecipeFeedDto
            {
                RecipeId = r.Id,
                Title = r.Title,
                Description = r.Description,
                Region = r.Region,
                CreatedAt = r.CreatedAt,
                ImageUrl = r.ImageUrl,
                AuthorUsername = r.User!.Username,
                IsRemix = remixIds.Contains(r.Id),
                IsLikedByCurrentUser = currentUserId.HasValue && r.Likes.Any(l => l.UserId == currentUserId.Value),
                Likes = r.Likes.Count,
                IsRepostedByCurrentUser = repostedIds.Contains(r.Id),
                RepostCount = r.Reposts.Count,
                RemixCount = remixCountDict.TryGetValue(r.Id, out var count) ? count : 0,
                HasRemixedByCurrentUser = currentUserId.HasValue && userRemixedOriginals.Contains(r.Id),
                IsFollowing = currentUserId.HasValue && followingIds.Contains(r.UserId),
                Ingredients = r.Ingredients!.Select(i => new IngredientDto
                {
                    Name = i.Name,
                    Quantity = i.Quantity
                }).ToList(),
                IngredientNames = r.Ingredients!.Select(i => i.Name.ToLower()).ToList(),
                PostedBy = new PublicUserDto
                {
                    UserId = r.User.Id,
                    Username = r.User.Username,
                    DisplayName = r.User.DisplayName,
                    ProfileImageUrl = r.User.ProfileImageUrl
                }
            }).ToList();

            return Ok(feed);
        }

        [Authorize]
        [HttpGet("following")]
        public async Task<IActionResult> GetFollowingFeed()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var userGuid = Guid.Parse(userId);

            var allRemixes = await _context.RecipeRemixes
                .Include(r => r.RemixRecipe)
                .Where(r => r.OriginalRecipeId != null)
                .ToListAsync();

            var remixIds = allRemixes.Select(r => r.RemixRecipeId).ToList();

            var remixCountDict = allRemixes
                .Where(r => r.OriginalRecipeId.HasValue)
                .GroupBy(r => r.OriginalRecipeId.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            var userRemixedOriginals = allRemixes
                .Where(r => r.RemixRecipe.UserId == userGuid)
                .Select(r => r.OriginalRecipeId!.Value)
                .ToList();

            var followingIds = await _context.UserFollows
                .Where(f => f.FollowerId == userGuid)
                .Select(f => f.FollowingId)
                .ToListAsync();

            followingIds.Add(userGuid); // include own posts

            var originalRecipes = await _context.Recipes
                .Where(r => followingIds.Contains(r.UserId))
                .OrderByDescending(r => r.CreatedAt)
                .Include(r => r.Ingredients)
                .Include(r => r.User)
                .Include(r => r.Likes)
                .Include(r => r.Reposts)
                .ToListAsync();

            var reposts = await _context.RecipeReposts
                .Where(rp => followingIds.Contains(rp.UserId))
                .OrderByDescending(rp => rp.RepostedAt)
                .Include(rp => rp.Recipe)
                    .ThenInclude(r => r.User)
                .Include(rp => rp.Recipe)
                    .ThenInclude(r => r.Ingredients)
                .Include(rp => rp.Recipe)
                    .ThenInclude(r => r.Likes)
                .Include(rp => rp.Recipe)
                    .ThenInclude(r => r.Reposts)
                .ToListAsync();

            var userRepostedRecipeIds = await _context.RecipeReposts
                .Where(r => r.UserId == userGuid)
                .Select(r => r.RecipeId)
                .ToListAsync();

            var repostFeed = reposts.Select(rp => new RecipeFeedDto
            {
                RecipeId = rp.Recipe.Id,
                Title = rp.Recipe.Title,
                Description = rp.Recipe.Description,
                Region = rp.Recipe.Region,
                CreatedAt = rp.RepostedAt,
                ImageUrl = rp.Recipe.ImageUrl,
                AuthorUsername = rp.Recipe.User!.Username,
                IsRemix = remixIds.Contains(rp.Recipe.Id),
                IsLikedByCurrentUser = rp.Recipe.Likes.Any(l => l.UserId == userGuid),
                Likes = rp.Recipe.Likes.Count,
                IsRepostedByCurrentUser = userRepostedRecipeIds.Contains(rp.RecipeId),
                RepostCount = rp.Recipe.Reposts.Count,
                RemixCount = remixCountDict.TryGetValue(rp.Recipe.Id, out var count) ? count : 0,
                HasRemixedByCurrentUser = userRemixedOriginals.Contains(rp.Recipe.Id),
                IsFollowing = followingIds.Contains(rp.Recipe.UserId),
                Ingredients = rp.Recipe.Ingredients!.Select(i => new IngredientDto
                {
                    Name = i.Name,
                    Quantity = i.Quantity
                }).ToList(),
                IngredientNames = rp.Recipe.Ingredients!.Select(i => i.Name.ToLower()).ToList(),
                Type = "repost",
                PostedBy = new PublicUserDto
                {
                    UserId = rp.User.Id,
                    Username = rp.User.Username,
                    DisplayName = rp.User.DisplayName,
                    ProfileImageUrl = rp.User.ProfileImageUrl
                }
            });

            var originalFeed = originalRecipes.Select(r => new RecipeFeedDto
            {
                RecipeId = r.Id,
                Title = r.Title,
                Description = r.Description,
                Region = r.Region,
                CreatedAt = r.CreatedAt,
                ImageUrl = r.ImageUrl,
                AuthorUsername = r.User!.Username,
                IsRemix = remixIds.Contains(r.Id),
                IsLikedByCurrentUser = r.Likes.Any(l => l.UserId == userGuid),
                Likes = r.Likes.Count,
                IsRepostedByCurrentUser = r.Reposts.Any(p => p.UserId == userGuid),
                RepostCount = r.Reposts.Count,
                RemixCount = remixCountDict.TryGetValue(r.Id, out var count) ? count : 0,
                HasRemixedByCurrentUser = userRemixedOriginals.Contains(r.Id),
                IsFollowing = followingIds.Contains(r.UserId),
                Ingredients = r.Ingredients!.Select(i => new IngredientDto
                {
                    Name = i.Name,
                    Quantity = i.Quantity
                }).ToList(),
                IngredientNames = r.Ingredients!.Select(i => i.Name.ToLower()).ToList(),
                Type = "original",
                PostedBy = new PublicUserDto
                {
                    UserId = r.User.Id,
                    Username = r.User.Username,
                    DisplayName = r.User.DisplayName,
                    ProfileImageUrl = r.User.ProfileImageUrl
                }
            });

            var feed = repostFeed.Concat(originalFeed)
                .OrderByDescending(r => r.CreatedAt)
                .Take(30)
                .ToList();

            return Ok(feed);
        }
    }
}
