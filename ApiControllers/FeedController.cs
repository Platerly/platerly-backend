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
    /// <summary>
    /// B2C feed controller. Returns paginated recipe feeds for the Recipzee SPA.
    ///
    /// Base route: /api/Feed
    /// Auth requirement: GET /explore is [AllowAnonymous]; GET /following requires [Authorize].
    ///
    /// Key design notes:
    ///   • Allergen alerts for page recipes are fetched with a sequential <c>for</c> loop
    ///     (not <c>Task.WhenAll</c>) because EF Core's <see cref="Microsoft.EntityFrameworkCore.DbContext"/>
    ///     is not thread-safe — concurrent awaits on the same context instance throw
    ///     <see cref="System.InvalidOperationException"/>, which GlobalExceptionMiddleware
    ///     maps to a 400 response. The loop trades parallelism for correctness.
    ///   • "Hide my allergens" (<c>hideMyAllergens</c>) is silently ignored for free users;
    ///     only premium/trial accounts get actual filtering. Free users still see allergen
    ///     highlighting in the returned ingredient DTOs.
    ///   • Like and repost counts are fetched as aggregate DB queries after pagination,
    ///     not eagerly loaded, to avoid loading large row sets into memory.
    ///   • Ingredient severity is normalised via <see cref="NormKey"/> (lowercase, collapsed whitespace)
    ///     to match the recipe-details endpoint behaviour.
    /// </summary>
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

        // Canonical ingredient name key: lowercase, collapsed whitespace, single-space joined.
        // Matches the normalisation used in RecipeDetailsPage so ingredient severity lookups
        // in the feed align with lookups on the recipe detail view.
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

        /// <summary>
        /// Returns a paginated list of public recipes for the Explore page.
        ///
        /// Accessible to anonymous visitors (no auth required). When the caller is authenticated,
        /// allergen alerts are fetched for each recipe on the page and returned in the DTO
        /// (<c>MatchedAllergenTerms</c>, <c>MatchedAllergenSeverity</c>, per-ingredient <c>IsAllergen</c>).
        ///
        /// Filtering:
        ///   • <paramref name="difficulty"/>, <paramref name="recipeType"/>, <paramref name="tags"/>,
        ///     <paramref name="requireAllTags"/>, <paramref name="hideRemixes"/> — applied at DB level.
        ///   • <paramref name="hideMyAllergens"/> — silently ignored for free users; for premium/trial
        ///     users, recipes with any matched allergens are excluded from results
        ///     (<c>effectiveHideMyAllergens</c> gate).
        ///   • <paramref name="avoid"/> and <paramref name="minSeverity"/> affect display only —
        ///     they do not filter out recipes, only control which severity level triggers highlighting.
        ///
        /// IMPORTANT: allergen alerts are fetched via a sequential <c>for</c> loop (not
        /// <c>Task.WhenAll</c>) because EF Core's DbContext is not thread-safe. Concurrent
        /// awaits on the same instance throw <see cref="System.InvalidOperationException"/>.
        /// </summary>
        /// <returns>
        /// 200 with <c>List&lt;RecipeFeedDto&gt;</c> (empty list on no results);
        /// 400 on invalid filter parameter lengths.
        /// </returns>
        [AllowAnonymous]
        [HttpGet("explore")]
        [EnableRateLimiting("anonymous")]
        public async Task<IActionResult> GetExploreFeed(
            [FromQuery] string? difficulty = null,
            [FromQuery] string? recipeType = null,
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
            if (recipeType != null && recipeType.Length > 20)
                return BadRequest("Invalid recipeType parameter");

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

            // Who the current user follows (needed for IsFollowing in DTO)
            var followingIds = new List<Guid>();
            if (currentUserId.HasValue)
            {
                followingIds = await _context.UserFollows
                    .Where(f => f.FollowerId == currentUserId.Value)
                    .Select(f => f.FollowingId)
                    .ToListAsync();
            }

            // Build base query — Likes/Reposts are NOT eagerly loaded;
            // counts are fetched as targeted aggregates after pagination.
            // Use a Select projection to avoid loading full entity graphs (prevents N+1 for
            // navigation properties and avoids transferring columns not needed for the feed).
            var query = _context.Recipes
                .Where(r => r.User != null && !r.IsDeleted)
                .AsQueryable();

            // difficulty
            if (!string.IsNullOrWhiteSpace(difficulty))
            {
                var parsed = DifficultyExtensions.ParseDifficultyOrNull(difficulty);
                if (parsed.HasValue) query = query.Where(r => r.Difficulty == parsed.Value);
                else return Ok(new List<RecipeFeedDto>());
            }

            // recipe type (Meal, Shake, Dessert, etc.)
            if (!string.IsNullOrWhiteSpace(recipeType))
            {
                var parsed = DifficultyExtensions.ParseRecipeTypeOrNull(recipeType);
                if (parsed.HasValue) query = query.Where(r => r.Type == parsed.Value);
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

            // hideRemixes: use a DB-side subquery, avoids loading the entire remix table
            if (hideRemixes)
                query = query.Where(r => !_context.RecipeRemixes.Any(rm => rm.RemixRecipeId == r.Id));

            // Fetch the page using a Select projection — avoids loading full entity graphs
            // and eliminates the Cartesian-product JOIN explosion from Include chains.
            var publicRecipes = await query
                .OrderByDescending(r => r.CreatedAt)
                .Skip(skip)
                .Take(pageSize)
                .Select(r => new
                {
                    r.Id,
                    r.Title,
                    r.Slug,
                    r.Description,
                    r.Region,
                    r.ImageUrl,
                    r.PhotoCredit,
                    r.PhotoCreditUrl,
                    r.PrepTimeMinutes,
                    r.CookTimeMinutes,
                    r.Difficulty,
                    r.Type,
                    r.CreatedAt,
                    r.UserId,
                    Tags = r.RecipeTags.Select(rt => rt.Tag.Name).ToList(),
                    Ingredients = r.Ingredients.Select(i => new
                    {
                        IngredientId = i.Id,
                        i.Quantity,
                        i.GlobalIngredientId,
                        GlobalIngName = i.GlobalIngredient != null ? i.GlobalIngredient.Name : ""
                    }).ToList(),
                    AuthorUsername = r.User != null ? r.User.Username : "",
                    AuthorId = r.User != null ? r.User.Id : Guid.Empty,
                    AuthorDisplayName = r.User != null ? r.User.DisplayName : null,
                    AuthorProfileImageUrl = r.User != null ? r.User.ProfileImageUrl : null
                })
                .ToListAsync();

            if (publicRecipes.Count == 0)
                return Ok(new List<RecipeFeedDto>());

            var pageIds = publicRecipes.Select(r => r.Id).ToList();

            // ── Scoped remix data (only for recipes on this page) ─────────────
            // Two targeted queries — avoids Guid? .Value in OR which EF Core
            // can't reliably translate.

            // Which recipes on this page are themselves a remix?
            var remixAsRemix = await _context.RecipeRemixes
                .Where(rm => pageIds.Contains(rm.RemixRecipeId))
                .Select(rm => new { rm.RemixRecipeId, rm.OriginalRecipeId, RemixRecipeUserId = rm.RemixRecipe.UserId })
                .AsNoTracking()
                .ToListAsync();

            // How many remixes does each original recipe on this page have?
            var remixAsOriginal = await _context.RecipeRemixes
                .Where(rm => rm.OriginalRecipeId.HasValue && pageIds.Contains(rm.OriginalRecipeId.Value))
                .Select(rm => new { rm.OriginalRecipeId })
                .AsNoTracking()
                .ToListAsync();

            var remixIds = new HashSet<Guid>(remixAsRemix.Select(rm => rm.RemixRecipeId));

            var remixCountDict = remixAsOriginal
                .GroupBy(rm => rm.OriginalRecipeId!.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            // ── Scoped like/repost counts (aggregate queries, no row loading) ──
            var likeCountMap = (await _context.RecipeLikes
                .Where(l => pageIds.Contains(l.RecipeId))
                .GroupBy(l => l.RecipeId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync())
                .ToDictionary(x => x.Key, x => x.Count);

            var repostCountMap = (await _context.RecipeReposts
                .Where(rp => pageIds.Contains(rp.RecipeId))
                .GroupBy(rp => rp.RecipeId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync())
                .ToDictionary(x => x.Key, x => x.Count);

            var userLikedIds = new HashSet<Guid>();
            var repostedIds = new HashSet<Guid>();
            var userRemixedOriginals = new HashSet<Guid>();

            if (currentUserId.HasValue)
            {
                userLikedIds = new HashSet<Guid>(await _context.RecipeLikes
                    .Where(l => l.UserId == currentUserId.Value && pageIds.Contains(l.RecipeId))
                    .Select(l => l.RecipeId)
                    .ToListAsync());

                repostedIds = new HashSet<Guid>(await _context.RecipeReposts
                    .Where(rp => rp.UserId == currentUserId.Value && pageIds.Contains(rp.RecipeId))
                    .Select(rp => rp.RecipeId)
                    .ToListAsync());

                userRemixedOriginals = new HashSet<Guid>(remixAsRemix
                    .Where(rm => rm.RemixRecipeUserId == currentUserId.Value && rm.OriginalRecipeId.HasValue)
                    .Select(rm => rm.OriginalRecipeId!.Value));
            }

            // ── Alerts: parallel fetch, only for this page ─────────────────────
            var severityThreshold = Rank(minSeverity);
            var alertsByRecipe = new Dictionary<Guid, Dictionary<string, string>>();
            var hasAnyByRecipe = new Dictionary<Guid, bool>();

            if (currentUserId.HasValue && pageIds.Count > 0)
            {
                // Sequential — DbContext is not thread-safe; concurrent awaits on the same
                // instance throw InvalidOperationException in EF Core.
                for (int i = 0; i < publicRecipes.Count; i++)
                {
                    var alertResponse = await _alerts.GetAlertsForRecipeAsync(publicRecipes[i].Id, currentUserId.Value);
                    var map = BuildIngredientSeverityMap(alertResponse);
                    var filtered = map
                        .Where(kv => Rank(kv.Value) >= severityThreshold)
                        .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
                    alertsByRecipe[publicRecipes[i].Id] = filtered;
                    hasAnyByRecipe[publicRecipes[i].Id] = filtered.Count > 0;
                }

                // hideMyAllergens is a premium/trial feature — silently ignore for free users
                var feedUser = await _context.Users
                    .AsNoTracking()
                    .Where(u => u.Id == currentUserId.Value)
                    .Select(u => new { u.IsPremium, u.TrialEndUtc })
                    .FirstOrDefaultAsync();

                var effectiveHideMyAllergens = hideMyAllergens
                    && feedUser != null
                    && (feedUser.IsPremium
                        || (feedUser.TrialEndUtc.HasValue && feedUser.TrialEndUtc.Value > DateTimeOffset.UtcNow));

                if (effectiveHideMyAllergens)
                {
                    publicRecipes = publicRecipes
                        .Where(r => !hasAnyByRecipe.GetValueOrDefault(r.Id, false))
                        .ToList();
                }
            }

            // ── Build DTOs ─────────────────────────────────────────────────────
            var feed = publicRecipes.Select(r =>
            {
                alertsByRecipe.TryGetValue(r.Id, out var matchedSeverities);
                matchedSeverities ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                var ingredientNames = r.Ingredients
                    .Select(i => i.GlobalIngName.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                var ingredientDtos = r.Ingredients.Select(i =>
                {
                    var name = i.GlobalIngName.Trim();
                    var key = NormKey(name);
                    matchedSeverities.TryGetValue(key, out var sev);
                    return new IngredientDto
                    {
                        IngredientId = i.IngredientId,
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
                    Slug = r.Slug,
                    Description = r.Description,
                    Region = r.Region,
                    CreatedAt = r.CreatedAt,
                    ImageUrl = r.ImageUrl,
                    PhotoCredit = r.PhotoCredit,
                    PhotoCreditUrl = r.PhotoCreditUrl,
                    AuthorUsername = r.AuthorUsername,
                    IsRemix = remixIds.Contains(r.Id),
                    IsLikedByCurrentUser = userLikedIds.Contains(r.Id),
                    Likes = likeCountMap.GetValueOrDefault(r.Id, 0),
                    IsRepostedByCurrentUser = repostedIds.Contains(r.Id),
                    RepostCount = repostCountMap.GetValueOrDefault(r.Id, 0),
                    RemixCount = remixCountDict.GetValueOrDefault(r.Id, 0),
                    HasRemixedByCurrentUser = userRemixedOriginals.Contains(r.Id),
                    IsFollowing = currentUserId.HasValue && followingIds.Contains(r.UserId),
                    PrepTimeMinutes = r.PrepTimeMinutes,
                    CookTimeMinutes = r.CookTimeMinutes,
                    Difficulty = r.Difficulty.ToDisplayString(),
                    RecipeType = r.Type.ToDisplayString(),
                    Tags = r.Tags,
                    Ingredients = ingredientDtos,
                    IngredientNames = ingredientNames,
                    MatchedAllergenTerms = matchedSeverities.Keys.ToList(),
                    MatchedAllergenSeverity = matchedSeverities,
                    PostedBy = new PublicUserDto
                    {
                        UserId = r.AuthorId,
                        Username = r.AuthorUsername,
                        DisplayName = r.AuthorDisplayName,
                        ProfileImageUrl = r.AuthorProfileImageUrl
                    }
                };
            }).ToList();

            return Ok(feed);
        }

        // -------------------------- FOLLOWING FEED ---------------------------

        /// <summary>
        /// Returns a paginated chronological feed of recipes and reposts from users the
        /// authenticated user follows (plus their own recipes).
        ///
        /// Implementation notes:
        ///   • Originals and reposts are fetched as two separate bounded queries (capped at
        ///     <c>max(pageSize × 10, 200)</c> rows each) then merged and paginated in memory
        ///     by descending date — this avoids an unbounded UNION query against the DB.
        ///   • The user's own ID is added to <c>followingIds</c> so their own recipes
        ///     appear in the feed without needing a separate query.
        ///   • Allergen alerts are fetched via a sequential <c>for</c> loop for the same
        ///     thread-safety reason as the explore feed (EF Core DbContext is not thread-safe).
        /// </summary>
        /// <param name="minSeverity">
        /// Minimum allergen severity threshold for highlighting (low/medium/high). Default: "medium".
        /// </param>
        /// <returns>
        /// 200 with <c>List&lt;RecipeFeedDto&gt;</c> (empty list when following no one or no content);
        /// 401 if not authenticated.
        /// </returns>
        [Authorize]
        [HttpGet("following")]
        public async Task<IActionResult> GetFollowingFeed(
            [FromQuery] string? minSeverity = "medium",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 50);
            var skip = (page - 1) * pageSize;
            if (!User.TryGetUserId(out var userGuid))
                return Unauthorized();

            var severityThreshold = Rank(minSeverity);

            var followingIds = await _context.UserFollows
                .Where(f => f.FollowerId == userGuid)
                .Select(f => f.FollowingId)
                .ToListAsync();
            followingIds.Add(userGuid);

            // Cap the window loaded from DB — enough to paginate through many pages
            // without loading unbounded rows for users who follow many prolific creators.
            var cap = Math.Max(pageSize * 10, 200);

            // Originals — no Likes/Reposts includes; counts fetched separately.
            // AsSplitQuery prevents Cartesian product explosion from multiple collection includes.
            var originalRecipes = await _context.Recipes
                .Where(r => followingIds.Contains(r.UserId) && !r.IsDeleted)
                .OrderByDescending(r => r.CreatedAt)
                .Take(cap)
                .AsSplitQuery()
                .AsNoTracking()
                .Include(r => r.Ingredients).ThenInclude(i => i.GlobalIngredient)
                .Include(r => r.RecipeTags).ThenInclude(rt => rt.Tag)
                .Include(r => r.User)
                .ToListAsync();

            // Reposts — no Recipe.Likes/Reposts includes.
            // AsSplitQuery prevents Cartesian product explosion from multiple collection includes.
            var reposts = await _context.RecipeReposts
                .Where(rp => followingIds.Contains(rp.UserId))
                .OrderByDescending(rp => rp.RepostedAt)
                .Take(cap)
                .AsSplitQuery()
                .AsNoTracking()
                .Include(rp => rp.User)
                .Include(rp => rp.Recipe).ThenInclude(r => r.User)
                .Include(rp => rp.Recipe).ThenInclude(r => r.Ingredients).ThenInclude(i => i.GlobalIngredient)
                .Include(rp => rp.Recipe).ThenInclude(r => r.RecipeTags).ThenInclude(rt => rt.Tag)
                .ToListAsync();

            // Combine and paginate in memory (interleaved by date)
            var combined = originalRecipes
                .Select(r => (Recipe: r, SortDate: r.CreatedAt, Type: "original",
                    Poster: new PublicUserDto { UserId = r.User!.Id, Username = r.User.Username, DisplayName = r.User.DisplayName, ProfileImageUrl = r.User.ProfileImageUrl }))
                .Concat(reposts.Select(rp => (Recipe: rp.Recipe, SortDate: rp.RepostedAt, Type: "repost",
                    Poster: new PublicUserDto { UserId = rp.User.Id, Username = rp.User.Username, DisplayName = rp.User.DisplayName, ProfileImageUrl = rp.User.ProfileImageUrl })))
                .OrderByDescending(e => e.SortDate)
                .Skip(skip)
                .Take(pageSize)
                .ToList();

            if (combined.Count == 0)
                return Ok(new List<RecipeFeedDto>());

            var pagedIds = combined.Select(e => e.Recipe.Id).Distinct().ToList();

            // ── All auxiliary data scoped to the paged recipe IDs only ─────────

            var remixAsRemix = await _context.RecipeRemixes
                .Where(rm => pagedIds.Contains(rm.RemixRecipeId))
                .Select(rm => new { rm.RemixRecipeId, rm.OriginalRecipeId, RemixRecipeUserId = rm.RemixRecipe.UserId })
                .AsNoTracking()
                .ToListAsync();

            var remixAsOriginal = await _context.RecipeRemixes
                .Where(rm => rm.OriginalRecipeId.HasValue && pagedIds.Contains(rm.OriginalRecipeId.Value))
                .Select(rm => new { rm.OriginalRecipeId })
                .AsNoTracking()
                .ToListAsync();

            var remixIds = new HashSet<Guid>(remixAsRemix.Select(rm => rm.RemixRecipeId));

            var remixCountDict = remixAsOriginal
                .GroupBy(rm => rm.OriginalRecipeId!.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            var userRemixedOriginals = new HashSet<Guid>(remixAsRemix
                .Where(rm => rm.RemixRecipeUserId == userGuid && rm.OriginalRecipeId.HasValue)
                .Select(rm => rm.OriginalRecipeId!.Value));

            var likeCountMap = (await _context.RecipeLikes
                .Where(l => pagedIds.Contains(l.RecipeId))
                .GroupBy(l => l.RecipeId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync())
                .ToDictionary(x => x.Key, x => x.Count);

            var repostCountMap = (await _context.RecipeReposts
                .Where(rp => pagedIds.Contains(rp.RecipeId))
                .GroupBy(rp => rp.RecipeId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync())
                .ToDictionary(x => x.Key, x => x.Count);

            var userLikedIds = new HashSet<Guid>(await _context.RecipeLikes
                .Where(l => l.UserId == userGuid && pagedIds.Contains(l.RecipeId))
                .Select(l => l.RecipeId)
                .ToListAsync());

            var userRepostedIds = new HashSet<Guid>(await _context.RecipeReposts
                .Where(rp => rp.UserId == userGuid && pagedIds.Contains(rp.RecipeId))
                .Select(rp => rp.RecipeId)
                .ToListAsync());

            // ── Alerts: sequential — DbContext is not thread-safe ─────────────
            var alertsByRecipe = new Dictionary<Guid, Dictionary<string, string>>();
            for (int i = 0; i < pagedIds.Count; i++)
            {
                var alertResponse = await _alerts.GetAlertsForRecipeAsync(pagedIds[i], userGuid);
                var map = BuildIngredientSeverityMap(alertResponse);
                alertsByRecipe[pagedIds[i]] = map
                    .Where(kv => Rank(kv.Value) >= severityThreshold)
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            }

            // ── Build DTOs ─────────────────────────────────────────────────────
            var feed = combined.Select(entry =>
            {
                var r = entry.Recipe;
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
                    Slug = r.Slug,
                    Description = r.Description,
                    Region = r.Region,
                    CreatedAt = entry.SortDate,
                    ImageUrl = r.ImageUrl,
                    PhotoCredit = r.PhotoCredit,
                    PhotoCreditUrl = r.PhotoCreditUrl,
                    AuthorUsername = r.User!.Username,
                    IsRemix = remixIds.Contains(r.Id),
                    IsLikedByCurrentUser = userLikedIds.Contains(r.Id),
                    Likes = likeCountMap.GetValueOrDefault(r.Id, 0),
                    IsRepostedByCurrentUser = userRepostedIds.Contains(r.Id),
                    RepostCount = repostCountMap.GetValueOrDefault(r.Id, 0),
                    RemixCount = remixCountDict.GetValueOrDefault(r.Id, 0),
                    HasRemixedByCurrentUser = userRemixedOriginals.Contains(r.Id),
                    IsFollowing = followingIds.Contains(r.UserId),
                    PrepTimeMinutes = r.PrepTimeMinutes,
                    CookTimeMinutes = r.CookTimeMinutes,
                    Difficulty = r.Difficulty.ToDisplayString(),
                    RecipeType = r.Type.ToDisplayString(),
                    Tags = r.RecipeTags.Select(rt => rt.Tag.Name).ToList(),
                    Ingredients = ingredientDtos,
                    IngredientNames = ingredientNames,
                    MatchedAllergenTerms = matchedSeverities.Keys.ToList(),
                    MatchedAllergenSeverity = matchedSeverities,
                    PostedBy = entry.Poster,
                    Type = entry.Type
                };
            }).ToList();

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

            var nowUtc = DateTimeOffset.UtcNow;
            var isOnTrial = !user.IsPremium && user.TrialEndUtc.HasValue && user.TrialEndUtc.Value > nowUtc;

            if (!user.IsPremium && !isOnTrial)
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
        /// Get hidden gems - low view count but high engagement rate
        /// </summary>
        [AllowAnonymous]
        [HttpGet("hidden-gems")]
        public async Task<IActionResult> GetHiddenGems([FromQuery] int limit = 8)
        {
            limit = Math.Clamp(limit, 1, 20);

            var rawItems = await (
                from c in _context.RecipeTrendingCache.AsNoTracking()
                join r in _context.Recipes.AsNoTracking()
                    .Include(x => x.User)
                    on c.RecipeId equals r.Id
                where c.ViewCount > 0 && c.ViewCount < 500 && (c.LikeCount + c.SaveCount) >= 3
                select new
                {
                    r.Id,
                    r.Title,
                    r.ImageUrl,
                    AuthorUsername = r.User.Username,
                    c.ViewCount,
                    c.LikeCount,
                    c.SaveCount,
                    c.CommentCount
                })
                .ToListAsync();

            var items = rawItems
                .OrderByDescending(x => (x.LikeCount + x.SaveCount + x.CommentCount) / (double)x.ViewCount)
                .Take(limit)
                .Select(x => new
                {
                    x.Id,
                    x.Title,
                    x.ImageUrl,
                    x.AuthorUsername,
                    x.ViewCount,
                    x.LikeCount,
                    x.SaveCount,
                    x.CommentCount,
                    EngagementRate = Math.Round((x.LikeCount + x.SaveCount + x.CommentCount) * 100.0 / x.ViewCount, 1)
                })
                .ToList();

            return Ok(items);
        }

        /// <summary>
        /// Get dietary tag growth rates over the last 30 days vs prior 30 days
        /// </summary>
        [AllowAnonymous]
        [HttpGet("trending-tags")]
        public async Task<IActionResult> GetTrendingTags([FromQuery] int limit = 10)
        {
            limit = Math.Clamp(limit, 1, 50);

            var now = DateTime.UtcNow;
            var cutoff30 = now.AddDays(-30);
            var cutoff60 = now.AddDays(-60);

            var recipeTagData = await _context.RecipeTags
                .AsNoTracking()
                .Where(rt => !rt.Recipe.IsDeleted && rt.Recipe.CreatedAt >= cutoff60)
                .Select(rt => new { TagName = rt.Tag.Name, rt.Recipe.CreatedAt })
                .ToListAsync();

            var tagStats = recipeTagData
                .GroupBy(x => x.TagName)
                .Select(g => new
                {
                    Name = g.Key,
                    TotalRecipes = g.Count(),
                    Last30Days = g.Count(x => x.CreatedAt >= cutoff30),
                    Prev30Days = g.Count(x => x.CreatedAt < cutoff30)
                })
                .Where(t => t.TotalRecipes >= 3)
                .OrderByDescending(t => t.TotalRecipes)
                .Take(limit)
                .ToList();

            var result = tagStats.Select(t =>
            {
                var growthRate = (t.Last30Days - t.Prev30Days) * 100.0 / Math.Max(t.Prev30Days, 1);
                return new
                {
                    t.Name,
                    t.TotalRecipes,
                    t.Last30Days,
                    GrowthRate = Math.Round(growthRate, 1),
                    Trend = growthRate > 10 ? "rising" : growthRate < -10 ? "falling" : "stable"
                };
            }).ToList();

            return Ok(result);
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
