using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Helpers;
using Platerly.Api.Services.Allergy;
using Platerly.Api.Services.Ingredients;
using Platerly.Api.Extensions;
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
        [EnableRateLimiting("anonymous")]
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
            // Input validation for string parameters
            if (tags != null && tags.Length > 500)
                return BadRequest("Tags parameter too long (max 500 characters)");
            if (avoid != null && avoid.Length > 500)
                return BadRequest("Avoid parameter too long (max 500 characters)");
            if (difficulty != null && difficulty.Length > 20)
                return BadRequest("Invalid difficulty parameter");

            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 50);
            var skip = (page - 1) * pageSize;

            Guid? currentUserId = null;
            if (User.Identity?.IsAuthenticated == true)
            {
                var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (Guid.TryParse(userIdString, out var parsedId))
                    currentUserId = parsedId;
            }

            // remix metadata - project only needed fields instead of loading full entities
            var allRemixData = await _context.RecipeRemixes
                .Where(r => r.OriginalRecipeId != null)
                .Select(r => new
                {   
                    r.RemixRecipeId,
                    r.OriginalRecipeId,
                    RemixRecipeUserId = r.RemixRecipe.UserId
                })
                .ToListAsync();

            var remixIds = allRemixData.Select(r => r.RemixRecipeId).ToList();

            var remixCountDict = allRemixData
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

                userRemixedOriginals = allRemixData
                    .Where(r => r.RemixRecipeUserId == currentUserId.Value && r.OriginalRecipeId.HasValue)
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
            if (!User.TryGetUserId(out var userGuid))
                return Unauthorized();

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

        /// <summary>
        /// Get trending recipes with enhanced data including engagement metrics and rank changes
        /// </summary>
        [AllowAnonymous]
        [HttpGet("trending-top")]
        public async Task<IActionResult> GetTrendingTop(
            [FromQuery] int limit = 5,
            [FromQuery] string period = "week",
            [FromQuery] string? category = null,
            [FromQuery] string? region = null)
        {
            limit = Math.Clamp(limit, 1, 20);

            var query = from c in _context.RecipeTrendingCache.AsNoTracking()
                        join r in _context.Recipes.AsNoTracking()
                            .Include(x => x.User)
                            .Include(x => x.RecipeTags).ThenInclude(rt => rt.Tag)
                            on c.RecipeId equals r.Id
                        select new { Cache = c, Recipe = r };

            // Filter by region if specified
            if (!string.IsNullOrWhiteSpace(region))
            {
                query = query.Where(x => x.Recipe.Region == region);
            }

            // Filter by category (cuisine, difficulty, etc.)
            if (!string.IsNullOrWhiteSpace(category))
            {
                var categoryLower = category.ToLowerInvariant();
                query = query.Where(x =>
                    x.Recipe.Region != null && x.Recipe.Region.ToLower().Contains(categoryLower) ||
                    x.Recipe.Difficulty.ToString().ToLower() == categoryLower ||
                    x.Recipe.RecipeTags.Any(rt => rt.Tag.Name.ToLower().Contains(categoryLower)));
            }

            var items = await query
                .OrderBy(x => x.Cache.Rank)
                .ThenByDescending(x => x.Cache.Score)
                .Take(limit)
                .Select(x => new
                {
                    x.Recipe.Id,
                    x.Recipe.Title,
                    x.Recipe.ImageUrl,
                    x.Recipe.CreatedAt,
                    x.Recipe.Region,
                    Difficulty = x.Recipe.Difficulty.ToString(),
                    Username = x.Recipe.User.Username,
                    Rank = x.Cache.Rank,
                    Score = x.Cache.Score,
                    RankDelta = x.Cache.RankDelta,
                    VelocityScore = x.Cache.VelocityScore,
                    LikeCount = x.Cache.LikeCount,
                    RemixCount = x.Cache.RemixCount,
                    CommentCount = x.Cache.CommentCount,
                    ViewCount = x.Cache.ViewCount,
                    SaveCount = x.Cache.SaveCount,
                    Tags = x.Recipe.RecipeTags.Select(rt => rt.Tag.Name).ToList()
                })
                .ToListAsync();

            var computedAt = await _context.RecipeTrendingCache.AsNoTracking()
                .Select(x => x.ComputedAt)
                .FirstOrDefaultAsync();

            return Ok(new
            {
                computedAt,
                period,
                region,
                category,
                items
            });
        }

        /// <summary>
        /// Get rising stars - new recipes (less than 7 days old) with high velocity
        /// </summary>
        [AllowAnonymous]
        [HttpGet("rising-stars")]
        public async Task<IActionResult> GetRisingStars([FromQuery] int limit = 5)
        {
            limit = Math.Clamp(limit, 1, 20);
            var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);

            var items = await (
                from c in _context.RecipeTrendingCache.AsNoTracking()
                join r in _context.Recipes.AsNoTracking()
                    .Include(x => x.User)
                    on c.RecipeId equals r.Id
                where r.CreatedAt >= sevenDaysAgo && c.VelocityScore > 1.2
                orderby c.VelocityScore descending, c.Score descending
                select new
                {
                    r.Id,
                    r.Title,
                    r.ImageUrl,
                    r.CreatedAt,
                    Username = r.User.Username,
                    Rank = c.Rank,
                    Score = c.Score,
                    VelocityScore = c.VelocityScore,
                    LikeCount = c.LikeCount,
                    RemixCount = c.RemixCount,
                    DaysOld = (int)(DateTime.UtcNow - r.CreatedAt).TotalDays
                })
                .Take(limit)
                .ToListAsync();

            return Ok(new { items });
        }

        /// <summary>
        /// Get trending recipes filtered by specific category values
        /// </summary>
        [AllowAnonymous]
        [HttpGet("trending-by-category")]
        public async Task<IActionResult> GetTrendingByCategory(
            [FromQuery] string type,     // cuisine, difficulty, tag
            [FromQuery] string value,
            [FromQuery] int limit = 10)
        {
            limit = Math.Clamp(limit, 1, 20);

            if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(value))
                return BadRequest("Both 'type' and 'value' parameters are required.");

            var query = from c in _context.RecipeTrendingCache.AsNoTracking()
                        join r in _context.Recipes.AsNoTracking()
                            .Include(x => x.User)
                            .Include(x => x.RecipeTags).ThenInclude(rt => rt.Tag)
                            on c.RecipeId equals r.Id
                        select new { Cache = c, Recipe = r };

            var valueLower = value.ToLowerInvariant();

            switch (type.ToLowerInvariant())
            {
                case "cuisine":
                case "region":
                    query = query.Where(x => x.Recipe.Region != null && x.Recipe.Region.ToLower().Contains(valueLower));
                    break;
                case "difficulty":
                    query = query.Where(x => x.Recipe.Difficulty.ToString().ToLower() == valueLower);
                    break;
                case "tag":
                    query = query.Where(x => x.Recipe.RecipeTags.Any(rt => rt.Tag.Name.ToLower() == valueLower));
                    break;
                default:
                    return BadRequest($"Invalid category type: {type}. Allowed: cuisine, difficulty, tag");
            }

            var items = await query
                .OrderBy(x => x.Cache.Rank)
                .ThenByDescending(x => x.Cache.Score)
                .Take(limit)
                .Select(x => new
                {
                    x.Recipe.Id,
                    x.Recipe.Title,
                    x.Recipe.ImageUrl,
                    x.Recipe.CreatedAt,
                    x.Recipe.Region,
                    Difficulty = x.Recipe.Difficulty.ToString(),
                    Username = x.Recipe.User.Username,
                    Rank = x.Cache.Rank,
                    Score = x.Cache.Score,
                    LikeCount = x.Cache.LikeCount,
                    RemixCount = x.Cache.RemixCount
                })
                .ToListAsync();

            return Ok(new { type, value, items });
        }

        /// <summary>
        /// Get trending creators based on follower growth and recipe engagement
        /// </summary>
        [AllowAnonymous]
        [HttpGet("trending-creators")]
        public async Task<IActionResult> GetTrendingCreators([FromQuery] int limit = 10)
        {
            limit = Math.Clamp(limit, 1, 20);

            // Check if we have cached creator trending data
            var cachedCreators = await _context.UserTrendingCache.AsNoTracking()
                .OrderBy(x => x.Rank)
                .Take(limit)
                .Join(_context.Users.AsNoTracking(),
                    c => c.UserId,
                    u => u.Id,
                    (c, u) => new
                    {
                        u.Id,
                        u.Username,
                        u.DisplayName,
                        u.ProfileImageUrl,
                        u.Bio,
                        c.Rank,
                        c.Score,
                        c.RankDelta,
                        c.FollowerGrowth,
                        c.RecipeEngagementTotal,
                        c.TrendingRecipeCount,
                        c.ComputedAt
                    })
                .ToListAsync();

            if (cachedCreators.Count > 0)
            {
                return Ok(new { items = cachedCreators });
            }

            // Fallback: compute trending creators on-the-fly
            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

            // Get users with recipes in trending cache
            var trendingRecipeAuthors = await (
                from c in _context.RecipeTrendingCache.AsNoTracking()
                join r in _context.Recipes.AsNoTracking() on c.RecipeId equals r.Id
                group new { c, r } by r.UserId into g
                select new
                {
                    UserId = g.Key,
                    TrendingRecipeCount = g.Count(),
                    TotalEngagement = g.Sum(x => x.c.LikeCount + x.c.CommentCount + x.c.RemixCount)
                })
                .OrderByDescending(x => x.TotalEngagement)
                .Take(limit)
                .ToListAsync();

            var userIds = trendingRecipeAuthors.Select(x => x.UserId).ToList();
            var users = await _context.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Username, u.DisplayName, u.ProfileImageUrl, u.Bio })
                .ToDictionaryAsync(u => u.Id);

            var items = trendingRecipeAuthors
                .Where(x => users.ContainsKey(x.UserId))
                .Select((x, i) => new
                {
                    users[x.UserId].Id,
                    users[x.UserId].Username,
                    users[x.UserId].DisplayName,
                    users[x.UserId].ProfileImageUrl,
                    users[x.UserId].Bio,
                    Rank = i + 1,
                    TrendingRecipeCount = x.TrendingRecipeCount,
                    TotalEngagement = x.TotalEngagement
                })
                .ToList();

            return Ok(new { items });
        }

        /// <summary>
        /// Get trending ingredients (Premium feature)
        /// </summary>
        [Authorize]
        [HttpGet("trending-ingredients")]
        public async Task<IActionResult> GetTrendingIngredients(
            [FromQuery] string? region = null,
            [FromQuery] int limit = 10,
            [FromServices] IIngredientAnalyticsService analyticsService = null!)
        {
            limit = Math.Clamp(limit, 1, 20);

            // Check premium status
            var userIdClaim = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var user = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                return Unauthorized();

            if (!user.IsPremium)
            {
                return StatusCode(402, new
                {
                    error = "premium_required",
                    code = "TRENDING_INGREDIENTS",
                    message = "Trending ingredients is a premium feature. Upgrade to access ingredient trend data."
                });
            }

            // Get trending ingredients from analytics service
            var request = new TrendingIngredientsRequest
            {
                Region = region,
                Days = 7,
                Take = limit
            };
            var trends = await analyticsService.GetTrendingIngredientsAsync(request);

            return Ok(new
            {
                region,
                items = trends.Trends
            });
        }

        /// <summary>
        /// Get available regions for filtering
        /// </summary>
        [AllowAnonymous]
        [HttpGet("trending-regions")]
        public async Task<IActionResult> GetTrendingRegions()
        {
            var regions = await _context.Recipes.AsNoTracking()
                .Where(r => !r.IsDeleted && r.Region != null && r.Region != "")
                .Select(r => r.Region)
                .Distinct()
                .OrderBy(r => r)
                .ToListAsync();

            return Ok(new { regions });
        }
    }
}
