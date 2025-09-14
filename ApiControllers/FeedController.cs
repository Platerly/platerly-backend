using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;
using Platerly.Api.Helpers;
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

        // --- EXPLORE FEED WITH FILTERS ---
        // GET /api/feed/explore?difficulty=easy&tags=vegan,low-nickel&requireAllTags=true&hideMyAllergens=true&hideRemixes=true&page=1&pageSize=30
        [AllowAnonymous]
        [HttpGet("explore")]
        public async Task<IActionResult> GetExploreFeed(
            [FromQuery] string? difficulty = null,
            [FromQuery] string? tags = null,
            [FromQuery] bool requireAllTags = false,
            [FromQuery] bool hideMyAllergens = false,
            [FromQuery] bool hideRemixes = false,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 30)
        {
            // normalize paging
            page = page <= 0 ? 1 : page;
            pageSize = pageSize <= 0 ? 30 : pageSize;
            var skip = (page - 1) * pageSize;

            Guid? currentUserId = null;
            var followingIds = new List<Guid>();
            var allergyKeywords = new List<string>();

            if (User.Identity?.IsAuthenticated == true)
            {
                var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (Guid.TryParse(userIdString, out Guid parsedId))
                {
                    currentUserId = parsedId;

                    // following list (for IsFollowing flag later)
                    followingIds = await _context.UserFollows
                        .Where(f => f.FollowerId == parsedId)
                        .Select(f => f.FollowingId)
                        .ToListAsync();

                    // gather allergy keywords if needed
                    if (hideMyAllergens)
                    {
                        var viewer = await _context.Users
                            .AsNoTracking()
                            .FirstOrDefaultAsync(u => u.Id == parsedId);

                        if (viewer != null && viewer.AllergyKeywords.Any())
                        {
                            allergyKeywords = viewer.AllergyKeywords
                                .Select(a => a.ToLower().Trim())
                                .Where(a => !string.IsNullOrWhiteSpace(a))
                                .Distinct()
                                .ToList();
                        }
                    }
                }
            }

            // Precompute remix data
            var allRemixes = await _context.RecipeRemixes
                .Include(r => r.RemixRecipe)
                .Where(r => r.OriginalRecipeId != null)
                .AsNoTracking()
                .ToListAsync();

            var remixIds = allRemixes.Select(r => r.RemixRecipeId).ToList();

            var remixCountDict = allRemixes
                .Where(r => r.OriginalRecipeId.HasValue)
                .GroupBy(r => r.OriginalRecipeId!.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            var userRemixedOriginals = currentUserId.HasValue
                ? allRemixes
                    .Where(r => r.RemixRecipe.UserId == currentUserId.Value && r.OriginalRecipeId.HasValue)
                    .Select(r => r.OriginalRecipeId!.Value)
                    .Distinct()
                    .ToList()
                : new List<Guid>();

            var repostedIds = currentUserId.HasValue
                ? await _context.RecipeReposts
                    .Where(r => r.UserId == currentUserId.Value)
                    .Select(r => r.RecipeId)
                    .ToListAsync()
                : new List<Guid>();

            // Base query
            var query = _context.Recipes
                .Where(r => r.User != null)
                .AsQueryable();

            // -- Difficulty filter
            
            if (!string.IsNullOrWhiteSpace(difficulty))
            {
                var parsed = DifficultyExtensions.ParseDifficultyOrNull(difficulty);
                if (parsed.HasValue)
                {
                    var target = parsed.Value; // enum
                    query = query.Where(r => r.Difficulty.HasValue && r.Difficulty == target);
                }
                else
                {
                    // invalid difficulty string => return empty
                    return Ok(new List<RecipeFeedDto>());
                }
            }



            // -- Tags filter (ANY by default; ALL if requireAllTags=true)
            List<string> tagList = new();
            if (!string.IsNullOrWhiteSpace(tags))
            {
                tagList = tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                              .Select(t => t.Trim().ToLower())
                              .Where(t => !string.IsNullOrWhiteSpace(t))
                              .Distinct()
                              .ToList();

                if (tagList.Count > 0)
                {
                    if (requireAllTags)
                    {
                        foreach (var t in tagList)
                        {
                            var tLocal = t; // EF capture
                            query = query.Where(r => r.Tags != null && EF.Functions.Like(r.Tags.ToLower(), "%" + tLocal + "%"));
                        }
                    }
                    else
                    {
                        query = query.Where(r =>
                            r.Tags != null &&
                            tagList.Any(t => EF.Functions.Like(r.Tags.ToLower(), "%" + t + "%")));
                    }
                }
            }

            // -- Hide remixes
            if (hideRemixes)
            {
                query = query.Where(r => !remixIds.Contains(r.Id));
            }

            // -- Hide my allergens (needs ingredient join)
            if (hideMyAllergens && allergyKeywords.Count > 0)
            {
                // We need ingredients for filtering; include now
                query = query.Include(r => r.Ingredients)
                             .ThenInclude(i => i.GlobalIngredient);

                query = query.Where(r =>
                    !r.Ingredients.Any(i =>
                        allergyKeywords.Any(a =>
                            ((i.GlobalIngredient != null ? i.GlobalIngredient.Name : (i.Name ?? ""))
                                .ToLower())
                                .Contains(a)
                        )));
            }
            else
            {
                // Still include for projection later (lightweight)
                query = query
                    .Include(r => r.Ingredients);
            }

            // Eager-load remaining navigation props used by projection
            query = query
                .Include(r => r.User)
                .Include(r => r.Likes)
                .Include(r => r.Reposts);

            // Sort, paginate
            query = query.OrderByDescending(r => r.CreatedAt)
                         .Skip(skip)
                         .Take(pageSize);

            var publicRecipes = await query.AsNoTracking().ToListAsync();

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
                PrepTimeMinutes = r.PrepTimeMinutes,
                CookTimeMinutes = r.CookTimeMinutes,
                Difficulty = r.Difficulty.ToDisplayString(),
                Tags = r.Tags != null ? r.Tags.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList() : new List<string>(),
                Ingredients = r.Ingredients!.Select(i => new IngredientDto
                {
                    Name = i.GlobalIngredient != null ? i.GlobalIngredient.Name : i.Name,
                    Quantity = i.Quantity
                }).ToList(),
                IngredientNames = r.Ingredients!.Select(i => (i.GlobalIngredient != null ? i.GlobalIngredient.Name : i.Name)!.ToLower()).ToList(),
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

        // --- FOLLOWING FEED (unchanged behavior, but normalize Difficulty output) ---
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
                .Distinct()
                .ToList();

            var followingIds = await _context.UserFollows
                .Where(f => f.FollowerId == userGuid)
                .Select(f => f.FollowingId)
                .ToListAsync();

            followingIds.Add(userGuid); // include own posts

            var originalRecipes = await _context.Recipes
                .Where(r => followingIds.Contains(r.UserId))
                .OrderByDescending(r => r.CreatedAt)
                .Include(r => r.Ingredients).ThenInclude(i => i.GlobalIngredient)
                .Include(r => r.User)
                .Include(r => r.Likes)
                .Include(r => r.Reposts)
                .ToListAsync();

            var reposts = await _context.RecipeReposts
                .Where(rp => followingIds.Contains(rp.UserId))
                .OrderByDescending(rp => rp.RepostedAt)
                .Include(rp => rp.Recipe).ThenInclude(r => r.User)
                .Include(rp => rp.Recipe).ThenInclude(r => r.Ingredients).ThenInclude(i => i.GlobalIngredient)
                .Include(rp => rp.Recipe).ThenInclude(r => r.Likes)
                .Include(rp => rp.Recipe).ThenInclude(r => r.Reposts)
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
                PrepTimeMinutes = rp.Recipe.PrepTimeMinutes,
                CookTimeMinutes = rp.Recipe.CookTimeMinutes,
                Difficulty = rp.Recipe.Difficulty.ToDisplayString(),
                Tags = rp.Recipe.Tags != null ? rp.Recipe.Tags.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList() : new List<string>(),
                Ingredients = rp.Recipe.Ingredients!.Select(i => new IngredientDto
                {
                    Name = i.GlobalIngredient != null ? i.GlobalIngredient.Name : i.Name,
                    Quantity = i.Quantity
                }).ToList(),
                IngredientNames = rp.Recipe.Ingredients!.Select(i => (i.GlobalIngredient != null ? i.GlobalIngredient.Name : i.Name)!.ToLower()).ToList(),
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
                PrepTimeMinutes = r.PrepTimeMinutes,
                CookTimeMinutes = r.CookTimeMinutes,
                Difficulty = r.Difficulty.ToDisplayString(),
                Tags = r.Tags != null ? r.Tags.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList() : new List<string>(),
                Ingredients = r.Ingredients!.Select(i => new IngredientDto
                {
                    Name = i.GlobalIngredient != null ? i.GlobalIngredient.Name : i.Name,
                    Quantity = i.Quantity
                }).ToList(),
                IngredientNames = r.Ingredients!.Select(i => (i.GlobalIngredient != null ? i.GlobalIngredient.Name : i.Name)!.ToLower()).ToList(),
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
