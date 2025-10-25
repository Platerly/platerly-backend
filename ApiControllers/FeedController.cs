using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Helpers;
using Platerly.Api.Services;
using System.Security.Claims;
using static Platerly.Api.Services.AllergenResolver;

namespace Platerly.Api.ApiControllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FeedController : ControllerBase
    {
        private readonly PlaterlyDbContext _context;
        private readonly IAllergenResolver _allergenResolver;

        public FeedController(PlaterlyDbContext context, IAllergenResolver allergenResolver)
        {
            _context = context;
            _allergenResolver = allergenResolver;
        }

        // ----------------- helpers (singular/plural tolerant) -----------------
        private static string Root(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant();
            if (s.EndsWith("ies")) return s[..^3] + "y";  // berries -> berry
            if (s.EndsWith("es")) return s[..^2];         // tomatoes -> tomato (rough)
            if (s.EndsWith("s")) return s[..^1];         // almonds -> almond
            return s;
        }

        private static bool LooseMatch(string ingLower, string termLower)
        {
            if (string.IsNullOrWhiteSpace(ingLower) || string.IsNullOrWhiteSpace(termLower)) return false;

            // direct contains either way
            if (ingLower.Contains(termLower) || termLower.Contains(ingLower)) return true;

            // crude singularization both sides
            var rIng = Root(ingLower);
            var rTerm = Root(termLower);
            return rIng.Contains(rTerm) || rTerm.Contains(rIng);
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
            [FromQuery] string? avoid = null,
            [FromQuery] string? minSeverity = "medium", // "low" | "medium" | "high"
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 30)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Max(pageSize, 30);
            var skip = (page - 1) * pageSize;

            Guid? currentUserId = null;
            var followingIds = new List<Guid>();
            var selectedAllergyNames = new List<string>();

            // auth context: follows + allergy names via UserAllergies -> Allergies
            if (User.Identity?.IsAuthenticated == true)
            {
                var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (Guid.TryParse(userIdString, out var parsedId))
                {
                    currentUserId = parsedId;

                    followingIds = await _context.UserFollows
                        .Where(f => f.FollowerId == parsedId)
                        .Select(f => f.FollowingId)
                        .ToListAsync();

                    selectedAllergyNames = await (
                        from ua in _context.UserAllergies
                        join a in _context.Allergies on ua.AllergyId equals a.Id
                        where ua.UserId == parsedId
                        select a.Name
                    ).AsNoTracking().ToListAsync();
                }
            }

            // manual avoid terms (?avoid=nickel,chocolate)
            var avoidTerms = (avoid ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToLowerInvariant())
                .Distinct()
                .ToList();

            // base keywords = user allergies + avoid query
            var baseKeywords = selectedAllergyNames
                .Concat(avoidTerms)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList();

            // severity-aware expansion
            // NOTE: IAllergenResolver should expose ExpandWithSeverity + FilterByMinSeverity.
            var expandedWithSeverity = (_allergenResolver as AllergenResolver)?.ExpandWithSeverity(baseKeywords)
                                       ?? new List<(string term, Severity severity)>();
            var expandedAllergyKeywords = (_allergenResolver as AllergenResolver)?.FilterByMinSeverity(expandedWithSeverity, minSeverity!)
                                          ?? new List<string>();

            if (hideMyAllergens)
                Console.WriteLine($"[Explore] hideMyAllergens=true; minSeverity={minSeverity}; expanded=[{string.Join(", ", expandedAllergyKeywords)}]");

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

            // base query
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

            // hide remixes
            if (hideRemixes && remixIds.Count > 0)
                query = query.Where(r => !remixIds.Contains(r.Id));

            // hide allergens (server-side)
            if (hideMyAllergens && expandedAllergyKeywords.Count > 0)
            {
                var bannedRecipeIds = new HashSet<Guid>();
                foreach (var term in expandedAllergyKeywords)
                {
                    var pattern = $"%{term}%";
                    var ids = await _context.Ingredients
                        .Where(i => EF.Functions.Like(i.GlobalIngredient!.Name, pattern))
                        .Select(i => i.RecipeId)
                        .Distinct()
                        .ToListAsync();
                    foreach (var id in ids) bannedRecipeIds.Add(id);
                }
                if (bannedRecipeIds.Count > 0)
                    query = query.Where(r => !bannedRecipeIds.Contains(r.Id));
            }

            // fetch
            var publicRecipes = await query
                .OrderByDescending(r => r.CreatedAt)
                .Skip(skip)
                .Take(pageSize)
                .AsNoTracking()
                .ToListAsync();

            // DTO build (with severity map for frontend coloring)
            var feed = publicRecipes.Select(r =>
            {
                var ingNamesLower = r.Ingredients!
                    .Select(i => (i.GlobalIngredient?.Name ?? string.Empty).Trim().ToLowerInvariant())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                // highest severity per ingredient
                var matchedSeverities = new Dictionary<string, string>(); // key=ingredient(lower), value="high|medium|low"
                foreach (var ing in ingNamesLower)
                {
                    Severity? best = null;
                    foreach (var a in expandedWithSeverity)
                    {
                        if (LooseMatch(ing, (a.term ?? "").ToLowerInvariant()))
                        {
                            if (best == null || a.severity > best) best = a.severity;
                        }
                    }
                    if (best != null)
                        matchedSeverities[ing] = best.Value.ToString().ToLowerInvariant();
                }

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
                    IsRepostedByCurrentUser = repostedIds.Contains(r.Id),
                    RepostCount = r.Reposts.Count,
                    RemixCount = remixCountDict.TryGetValue(r.Id, out var count) ? count : 0,
                    HasRemixedByCurrentUser = currentUserId.HasValue && userRemixedOriginals.Contains(r.Id),
                    IsFollowing = currentUserId.HasValue && followingIds.Contains(r.UserId),
                    PrepTimeMinutes = r.PrepTimeMinutes,
                    CookTimeMinutes = r.CookTimeMinutes,
                    Difficulty = r.Difficulty.ToDisplayString(),
                    Tags = r.RecipeTags.Select(rt => rt.Tag.Name).ToList(),
                    Ingredients = r.Ingredients!.Select(i => new IngredientDto
                    {
                        Name = i.GlobalIngredient?.Name ?? string.Empty,
                        Quantity = i.Quantity
                    }).ToList(),
                    IngredientNames = ingNamesLower,
                    MatchedAllergenTerms = matchedSeverities.Keys.ToList(),
                    // 👇 severity map used by RecipeCard
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
        public async Task<IActionResult> GetFollowingFeed()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();
            var userGuid = Guid.Parse(userId);

            // user allergies
            var selectedAllergyNames = await (
                from ua in _context.UserAllergies
                join a in _context.Allergies on ua.AllergyId equals a.Id
                where ua.UserId == userGuid
                select a.Name
            ).AsNoTracking().ToListAsync();

            var expandedWithSeverity = (_allergenResolver as AllergenResolver)?.ExpandWithSeverity(selectedAllergyNames)
                                       ?? new List<(string term, Severity severity)>();

            // following (include self)
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
                .Include(rp => rp.Recipe).ThenInclude(r => r.User)
                .Include(rp => rp.Recipe).ThenInclude(r => r.Ingredients).ThenInclude(i => i.GlobalIngredient)
                .Include(rp => rp.Recipe).ThenInclude(r => r.RecipeTags).ThenInclude(rt => rt.Tag)
                .Include(rp => rp.Recipe).ThenInclude(r => r.Likes)
                .Include(rp => rp.Recipe).ThenInclude(r => r.Reposts)
                .ToListAsync();

            var repostFeed = reposts.Select(rp =>
            {
                var ingNamesLower = rp.Recipe.Ingredients!
                    .Select(i => (i.GlobalIngredient?.Name ?? string.Empty).Trim().ToLowerInvariant())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                var matchedSeverities = new Dictionary<string, string>();
                foreach (var ing in ingNamesLower)
                {
                    Severity? best = null;
                    foreach (var a in expandedWithSeverity)
                    {
                        if (LooseMatch(ing, (a.term ?? "").ToLowerInvariant()))
                        {
                            if (best == null || a.severity > best) best = a.severity;
                        }
                    }
                    if (best != null)
                        matchedSeverities[ing] = best.Value.ToString().ToLowerInvariant();
                }

                return new RecipeFeedDto
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
                    Tags = rp.Recipe.RecipeTags.Select(rt => rt.Tag.Name).ToList(),
                    Ingredients = rp.Recipe.Ingredients!.Select(i => new IngredientDto
                    {
                        Name = i.GlobalIngredient?.Name ?? string.Empty,
                        Quantity = i.Quantity
                    }).ToList(),
                    IngredientNames = ingNamesLower,
                    MatchedAllergenTerms = matchedSeverities.Keys.ToList(),
                    MatchedAllergenSeverity = matchedSeverities,
                    PostedBy = new PublicUserDto
                    {
                        UserId = rp.User.Id,
                        Username = rp.User.Username,
                        DisplayName = rp.User.DisplayName,
                        ProfileImageUrl = rp.User.ProfileImageUrl
                    },
                    Type = "repost"
                };
            });

            var originalFeed = originalRecipes.Select(r =>
            {
                var ingNamesLower = r.Ingredients!
                    .Select(i => (i.GlobalIngredient?.Name ?? string.Empty).Trim().ToLowerInvariant())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                var matchedSeverities = new Dictionary<string, string>();
                foreach (var ing in ingNamesLower)
                {
                    Severity? best = null;
                    foreach (var a in expandedWithSeverity)
                    {
                        if (LooseMatch(ing, (a.term ?? "").ToLowerInvariant()))
                        {
                            if (best == null || a.severity > best) best = a.severity;
                        }
                    }
                    if (best != null)
                        matchedSeverities[ing] = best.Value.ToString().ToLowerInvariant();
                }

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
                    Tags = r.RecipeTags.Select(rt => rt.Tag.Name).ToList(),
                    Ingredients = r.Ingredients!.Select(i => new IngredientDto
                    {
                        Name = i.GlobalIngredient?.Name ?? string.Empty,
                        Quantity = i.Quantity
                    }).ToList(),
                    IngredientNames = ingNamesLower,
                    MatchedAllergenTerms = matchedSeverities.Keys.ToList(),
                    MatchedAllergenSeverity = matchedSeverities,
                    PostedBy = new PublicUserDto
                    {
                        UserId = r.User.Id,
                        Username = r.User.Username,
                        DisplayName = r.User.DisplayName,
                        ProfileImageUrl = r.User.ProfileImageUrl
                    },
                    Type = "original"
                };
            });

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
