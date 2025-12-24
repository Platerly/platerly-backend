using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Helpers;
using Platerly.Api.Services.Allergy;
using System.Security.Claims;

namespace Platerly.Api.ApiControllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FeedController : ControllerBase
    {
        private readonly PlaterlyDbContext _context;
        private readonly IAllergyAlertService _alerts;

        public FeedController(PlaterlyDbContext context, IAllergyAlertService alerts)
        {
            _context = context;
            _alerts = alerts;
        }

        // canonical key used everywhere (matches Recipe Details endpoint behavior)
        private static string NormKey(string s)
            => (s ?? "").Trim().ToLowerInvariant().Replace("\t", " ").Replace("\n", " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .DefaultIfEmpty("")
                .Aggregate((a, b) => $"{a} {b}");

        private static int Rank(string? s)
            => s?.ToLowerInvariant() switch
            {
                "high" => 3,
                "medium" => 2,
                "low" => 1,
                _ => 1
            };

        private static string BestSeverity(string a, string b)
            => Rank(a) >= Rank(b) ? a : b;

        private static Dictionary<string, string> BuildIngredientSeverityMap(RecipeAlertsResponse? resp)
        {
            // key = NormKey(ingredientName), value = "high|medium|low"
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (resp?.Alerts == null) return map;

            foreach (var alert in resp.Alerts)
            {
                foreach (var ing in alert.Ingredients)
                {
                    var key = NormKey(ing.IngredientName);
                    if (string.IsNullOrWhiteSpace(key)) continue;

                    var sev = (ing.SeverityResolved ?? "Low").ToLowerInvariant();

                    if (!map.TryGetValue(key, out var cur))
                        map[key] = sev;
                    else
                        map[key] = BestSeverity(cur, sev);
                }
            }

            return map;
        }

        // --------------------------- EXPLORE FEED ----------------------------
        [AllowAnonymous]
        [HttpGet("explore")]
        public async Task<IActionResult> GetExploreFeed(
            [FromQuery] string? difficulty = null,
            [FromQuery] string? tags = null,
            [FromQuery] bool requireAllTags = false,
            [FromQuery] bool hideMyAllergens = false,
            [FromQuery] bool hideRemixes = false,
            [FromQuery] string? avoid = null,              // kept for backward compat
            [FromQuery] string? minSeverity = "medium",    // affects DISPLAY only
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 30)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Max(pageSize, 30);
            var skip = (page - 1) * pageSize;

            Guid? currentUserId = null;
            if (User.Identity?.IsAuthenticated == true)
            {
                var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (Guid.TryParse(userIdString, out var parsedId))
                    currentUserId = parsedId;
            }

            // remix metadata
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

            var followingIds = new List<Guid>();
            var userRemixedOriginals = new List<Guid>();
            var repostedIds = new List<Guid>();

            if (currentUserId.HasValue)
            {
                followingIds = await _context.UserFollows
                    .Where(f => f.FollowerId == currentUserId.Value)
                    .Select(f => f.FollowingId)
                    .ToListAsync();

                userRemixedOriginals = allRemixes
                    .Where(r => r.RemixRecipe.UserId == currentUserId.Value && r.OriginalRecipeId.HasValue)
                    .Select(r => r.OriginalRecipeId!.Value)
                    .Distinct()
                    .ToList();

                repostedIds = await _context.RecipeReposts
                    .Where(r => r.UserId == currentUserId.Value)
                    .Select(r => r.RecipeId)
                    .ToListAsync();
            }

            var query = _context.Recipes
                .Where(r => r.User != null && !r.IsDeleted)
                .Include(r => r.RecipeTags).ThenInclude(rt => rt.Tag)
                .Include(r => r.Ingredients).ThenInclude(i => i.GlobalIngredient)
                .Include(r => r.User)
                .Include(r => r.Likes)
                .Include(r => r.Reposts)
                .AsQueryable();

            // difficulty
            if (!string.IsNullOrWhiteSpace(difficulty))
            {
                var parsed = DifficultyExtensions.ParseDifficultyOrNull(difficulty);
                if (parsed.HasValue) query = query.Where(r => r.Difficulty == parsed.Value);
                else return Ok(new List<RecipeFeedDto>());
            }

            // tags
            if (!string.IsNullOrWhiteSpace(tags))
            {
                var tagList = tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim().ToLowerInvariant())
                    .Distinct()
                    .ToList();

                if (requireAllTags)
                {
                    foreach (var t in tagList)
                    {
                        var tLocal = t;
                        query = query.Where(r => r.RecipeTags.Any(rt => rt.Tag.Name.ToLower() == tLocal));
                    }
                }
                else
                {
                    query = query.Where(r => r.RecipeTags.Any(rt => tagList.Contains(rt.Tag.Name.ToLower())));
                }
            }

            if (hideRemixes && remixIds.Count > 0)
                query = query.Where(r => !remixIds.Contains(r.Id));

            // fetch
            var publicRecipes = await query
                .OrderByDescending(r => r.CreatedAt)
                .Skip(skip)
                .Take(pageSize)
                .AsNoTracking()
                .ToListAsync();

            // DB-driven allergy highlighting only if logged in
            var severityThreshold = Rank(minSeverity);
            var alertsByRecipe = new Dictionary<Guid, Dictionary<string, string>>();

            if (currentUserId.HasValue)
            {
                var hasAnyByRecipe = new Dictionary<Guid, bool>();

                foreach (var r in publicRecipes)
                {
                    var resp = await _alerts.GetAlertsForRecipeAsync(r.Id, currentUserId.Value);
                    var map = BuildIngredientSeverityMap(resp);

                    var filtered = map
                        .Where(kv => Rank(kv.Value) >= severityThreshold)
                        .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

                    alertsByRecipe[r.Id] = filtered;
                    hasAnyByRecipe[r.Id] = filtered.Count > 0;
                }

                if (hideMyAllergens)
                {
                    publicRecipes = publicRecipes
                        .Where(r => hasAnyByRecipe.TryGetValue(r.Id, out var hasAny) ? !hasAny : true)
                        .ToList();
                }
            }

            var feed = publicRecipes.Select(r =>
            {
                alertsByRecipe.TryGetValue(r.Id, out var matchedSeverities);
                matchedSeverities ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // ingredientNames should be original casing (frontend normalizes anyway)
                var ingredientNames = r.Ingredients!
                    .Select(i => (i.GlobalIngredient?.Name ?? string.Empty).Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                // Per-row flags for feed cards
                var ingredientDtos = r.Ingredients!.Select(i =>
                {
                    var name = (i.GlobalIngredient?.Name ?? string.Empty).Trim();
                    var key = NormKey(name);
                    matchedSeverities.TryGetValue(key, out var sev);

                    return new IngredientDto
                    {
                        IngredientId = i.Id,
                        GlobalIngredientId = i.GlobalIngredientId,
                        Name = name,
                        Quantity = i.Quantity,
                        IsAllergen = sev != null,
                        AllergySeverity = sev
                    };
                }).ToList();

                return new RecipeFeedDto
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
                    IsRepostedByCurrentUser = currentUserId.HasValue && repostedIds.Contains(r.Id),
                    RepostCount = r.Reposts.Count,
                    RemixCount = remixCountDict.TryGetValue(r.Id, out var count) ? count : 0,
                    HasRemixedByCurrentUser = currentUserId.HasValue && userRemixedOriginals.Contains(r.Id),
                    IsFollowing = currentUserId.HasValue && followingIds.Contains(r.UserId),
                    PrepTimeMinutes = r.PrepTimeMinutes,
                    CookTimeMinutes = r.CookTimeMinutes,
                    Difficulty = r.Difficulty.ToDisplayString(),
                    Tags = r.RecipeTags.Select(rt => rt.Tag.Name).ToList(),
                    Ingredients = ingredientDtos,
                    IngredientNames = ingredientNames,
                    MatchedAllergenTerms = matchedSeverities.Keys.ToList(),
                    MatchedAllergenSeverity = matchedSeverities,
                    PostedBy = new PublicUserDto
                    {
                        UserId = r.User.Id,
                        Username = r.User.Username,
                        DisplayName = r.User.DisplayName,
                        ProfileImageUrl = r.User.ProfileImageUrl
                    }
                };
            }).ToList();

            return Ok(feed);
        }

        // -------------------------- FOLLOWING FEED ---------------------------
        [Authorize]
        [HttpGet("following")]
        public async Task<IActionResult> GetFollowingFeed([FromQuery] string? minSeverity = "medium")
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();
            var userGuid = Guid.Parse(userId);

            var severityThreshold = Rank(minSeverity);

            var followingIds = await _context.UserFollows
                .Where(f => f.FollowerId == userGuid)
                .Select(f => f.FollowingId)
                .ToListAsync();
            followingIds.Add(userGuid);

            // remix metadata
            var allRemixes = await _context.RecipeRemixes
                .Include(r => r.RemixRecipe)
                .Where(r => r.OriginalRecipeId != null)
                .ToListAsync();

            var remixIds = allRemixes.Select(r => r.RemixRecipeId).ToList();
            var remixCountDict = allRemixes
                .Where(r => r.OriginalRecipeId.HasValue)
                .GroupBy(r => r.OriginalRecipeId!.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            var userRemixedOriginals = allRemixes
                .Where(r => r.RemixRecipe.UserId == userGuid)
                .Select(r => r.OriginalRecipeId!.Value)
                .Distinct()
                .ToList();

            var userRepostedRecipeIds = await _context.RecipeReposts
                .Where(r => r.UserId == userGuid)
                .Select(r => r.RecipeId)
                .ToListAsync();

            // originals
            var originalRecipes = await _context.Recipes
                .Where(r => followingIds.Contains(r.UserId) && !r.IsDeleted)
                .OrderByDescending(r => r.CreatedAt)
                .Include(r => r.Ingredients).ThenInclude(i => i.GlobalIngredient)
                .Include(r => r.RecipeTags).ThenInclude(rt => rt.Tag)
                .Include(r => r.User)
                .Include(r => r.Likes)
                .Include(r => r.Reposts)
                .ToListAsync();

            // reposts
            var reposts = await _context.RecipeReposts
                .Where(rp => followingIds.Contains(rp.UserId))
                .OrderByDescending(rp => rp.RepostedAt)
                .Include(rp => rp.User)
                .Include(rp => rp.Recipe).ThenInclude(r => r.User)
                .Include(rp => rp.Recipe).ThenInclude(r => r.Ingredients).ThenInclude(i => i.GlobalIngredient)
                .Include(rp => rp.Recipe).ThenInclude(r => r.RecipeTags).ThenInclude(rt => rt.Tag)
                .Include(rp => rp.Recipe).ThenInclude(r => r.Likes)
                .Include(rp => rp.Recipe).ThenInclude(r => r.Reposts)
                .ToListAsync();

            var allRecipeIds = originalRecipes.Select(r => r.Id)
                .Concat(reposts.Select(rp => rp.RecipeId))
                .Distinct()
                .ToList();

            // sequential
            var alertsByRecipe = new Dictionary<Guid, Dictionary<string, string>>();

            foreach (var id in allRecipeIds)
            {
                var resp = await _alerts.GetAlertsForRecipeAsync(id, userGuid);
                var map = BuildIngredientSeverityMap(resp);

                var filtered = map
                    .Where(kv => Rank(kv.Value) >= severityThreshold)
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

                alertsByRecipe[id] = filtered;
            }

            RecipeFeedDto BuildDto(Models.Recipe r, DateTime createdAt, PublicUserDto postedBy, string type)
            {
                alertsByRecipe.TryGetValue(r.Id, out var matchedSeverities);
                matchedSeverities ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                var ingredientNames = r.Ingredients!
                    .Select(i => (i.GlobalIngredient?.Name ?? string.Empty).Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                var ingredientDtos = r.Ingredients!.Select(i =>
                {
                    var name = (i.GlobalIngredient?.Name ?? string.Empty).Trim();
                    var key = NormKey(name);
                    matchedSeverities.TryGetValue(key, out var sev);

                    return new IngredientDto
                    {
                        IngredientId = i.Id,
                        GlobalIngredientId = i.GlobalIngredientId,
                        Name = name,
                        Quantity = i.Quantity,
                        IsAllergen = sev != null,
                        AllergySeverity = sev
                    };
                }).ToList();

                return new RecipeFeedDto
                {
                    RecipeId = r.Id,
                    Title = r.Title,
                    Description = r.Description,
                    Region = r.Region,
                    CreatedAt = createdAt,
                    ImageUrl = r.ImageUrl,
                    AuthorUsername = r.User!.Username,
                    IsRemix = remixIds.Contains(r.Id),
                    IsLikedByCurrentUser = r.Likes.Any(l => l.UserId == userGuid),
                    Likes = r.Likes.Count,
                    IsRepostedByCurrentUser = userRepostedRecipeIds.Contains(r.Id),
                    RepostCount = r.Reposts.Count,
                    RemixCount = remixCountDict.TryGetValue(r.Id, out var count) ? count : 0,
                    HasRemixedByCurrentUser = userRemixedOriginals.Contains(r.Id),
                    IsFollowing = followingIds.Contains(r.UserId),
                    PrepTimeMinutes = r.PrepTimeMinutes,
                    CookTimeMinutes = r.CookTimeMinutes,
                    Difficulty = r.Difficulty.ToDisplayString(),
                    Tags = r.RecipeTags.Select(rt => rt.Tag.Name).ToList(),
                    Ingredients = ingredientDtos,
                    IngredientNames = ingredientNames,
                    MatchedAllergenTerms = matchedSeverities.Keys.ToList(),
                    MatchedAllergenSeverity = matchedSeverities,
                    PostedBy = postedBy,
                    Type = type
                };
            }

            var repostFeed = reposts.Select(rp =>
                BuildDto(
                    rp.Recipe,
                    rp.RepostedAt,
                    new PublicUserDto
                    {
                        UserId = rp.User.Id,
                        Username = rp.User.Username,
                        DisplayName = rp.User.DisplayName,
                        ProfileImageUrl = rp.User.ProfileImageUrl
                    },
                    "repost"
                )
            );

            var originalFeed = originalRecipes.Select(r =>
                BuildDto(
                    r,
                    r.CreatedAt,
                    new PublicUserDto
                    {
                        UserId = r.User.Id,
                        Username = r.User.Username,
                        DisplayName = r.User.DisplayName,
                        ProfileImageUrl = r.User.ProfileImageUrl
                    },
                    "original"
                )
            );

            var feed = repostFeed.Concat(originalFeed)
                .OrderByDescending(r => r.CreatedAt)
                .Take(30)
                .ToList();

            return Ok(feed);
        }

        [AllowAnonymous]
        [HttpGet("trending-top")]
        public async Task<IActionResult> GetTrendingTop([FromQuery] int limit = 5)
        {
            limit = Math.Clamp(limit, 1, 12);

            var q =
                from c in _context.RecipeTrendingCache.AsNoTracking()
                join r in _context.Recipes.AsNoTracking()
                    .Include(x => x.User)
                    on c.RecipeId equals r.Id
                orderby c.Rank ascending, c.Score descending
                select new
                {
                    r.Id,
                    r.Title,
                    r.ImageUrl,
                    r.CreatedAt,
                    Username = r.User.Username,
                    Rank = c.Rank,
                    Score = c.Score
                };

            var items = await q.Take(limit).ToListAsync();

            return Ok(new
            {
                computedAt = (await _context.RecipeTrendingCache.AsNoTracking()
                    .OrderBy(x => x.ComputedAt).Select(x => x.ComputedAt).FirstOrDefaultAsync()),
                items
            });
        }
    }
}
