using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Helpers;
using Platerly.Api.Models;
using Platerly.Api.Services;
using Platerly.Api.Services.Allergy;
using Platerly.Api.Services.Ingredients;
using Platerly.Api.Services.Notifications;
using Platerly.Api.Extensions;
using SendGrid.Helpers.Mail;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Platerly.Api.ApiControllers
{
    // -------- multipart/form-data DTO (List<string> Tags) --------
    public class UpdateRecipeDto
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Instructions { get; set; }
        public string? Region { get; set; }
        public List<string>? Tags { get; set; }   // unified to List<string>
        public int? PrepTimeMinutes { get; set; }
        public int? CookTimeMinutes { get; set; }
        public string? Difficulty { get; set; }
        public string? Type { get; set; } // Meal, Shake, Dessert, etc.
        public IFormFile? Image { get; set; }
    }

    // -------- JSON DTOs (List<string> Tags) --------
    public class IngredientUpsertDto
    {
        public string Name { get; set; } = "";
        public string Quantity { get; set; } = "";
    }

    public class UpdateRecipeJsonDto
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Instructions { get; set; }
        public string? Region { get; set; }
        public List<string>? Tags { get; set; }   // unified to List<string>
        public int? PrepTimeMinutes { get; set; }
        public int? CookTimeMinutes { get; set; }
        public string? Difficulty { get; set; }
        public string? Type { get; set; } // Meal, Shake, Dessert, etc.
        public List<IngredientUpsertDto>? Ingredients { get; set; }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class RecipeController : ControllerBase
    {
        private readonly PlaterlyDbContext _context;
        private readonly IBlobService _blobService;
        private readonly ILogger<RecipeController> _log;
        private readonly IAllergenResolver _allergenResolver;
        private readonly IAllergyAlertService _alerts;
        private readonly INotificationService _notificationService;
        private readonly IIngredientValidationService _ingredientValidation;
        private readonly IIngredientUsageService _ingredientUsage;

        public RecipeController(
            PlaterlyDbContext context,
            IBlobService blobService,
            ILogger<RecipeController> log,
            IAllergenResolver allergenResolver,
            IAllergyAlertService alerts,
            INotificationService notificationService,
            IIngredientValidationService ingredientValidation,
            IIngredientUsageService ingredientUsage)
        {
            _context = context;
            _blobService = blobService;
            _log = log;
            _allergenResolver = allergenResolver;
            _alerts = alerts;
            _notificationService = notificationService;
            _ingredientValidation = ingredientValidation;
            _ingredientUsage = ingredientUsage;
        }

        private Guid? CurrentUserId() => User.GetUserIdOrNull();

        //--user allergies helper --//
        private async Task<List<string>> LoadUserAllergyKeywordsAsync(Guid userId)
        {
            var terms = await _context.UserAllergies
                .Where(ua => ua.UserId == userId)
                .Select(ua => ua.Allergy.Name)   // <-- change to .Allergy.Term if needed
                .ToListAsync();

            return (terms ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // -------------------- Tag helpers --------------------

        private static List<string> NormalizeNames(IEnumerable<string> names) =>
            (names ?? Enumerable.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// Ensure all tag names exist (case-insensitive). Creates missing rows.
        /// Race-safe against the unique index on Tags.NormalizedName.
        /// Returns lookup Name -> Id.
        /// </summary>
        private async Task<Dictionary<string, int>> EnsureTagsExistAsync(IEnumerable<string> names)
        {
            var wanted = NormalizeNames(names);
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (wanted.Count == 0) return result;

            var existing = await _context.Tags
                .Where(t => wanted.Contains(t.Name))
                .ToListAsync();

            foreach (var t in existing)
                result[t.Name] = t.Id;

            var missing = wanted.Where(n => !result.ContainsKey(n)).ToList();
            foreach (var name in missing)
            {
                try
                {
                    var tag = new Tag { Name = name };
                    _context.Tags.Add(tag);
                    await _context.SaveChangesAsync();
                    result[name] = tag.Id;
                }
                catch (DbUpdateException)
                {
                    var id = await _context.Tags
                        .Where(t => t.Name == name)
                        .Select(t => t.Id)
                        .FirstOrDefaultAsync();
                    if (id == 0) throw;
                    result[name] = id;
                }
            }

            return result;
        }

        private static void ReplaceTagsWithNames(Recipe recipe, IEnumerable<string> tagNames, IReadOnlyDictionary<string, int> tagLookupByName)
        {
            if (recipe == null) return;

            var names = NormalizeNames(tagNames);

            recipe.RecipeTags ??= new List<RecipeTag>();
            recipe.RecipeTags.Clear();

            foreach (var name in names)
            {
                if (tagLookupByName.TryGetValue(name, out var tagId))
                {
                    recipe.RecipeTags.Add(new RecipeTag { TagId = tagId });
                }
            }
        }

        private static IReadOnlyList<string> ToTagNameArray(Recipe recipe) =>
            recipe?.RecipeTags?
                .Select(rt => rt.Tag?.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            ?? new List<string>();

        // ---------------------- Allergy helpers ----------------------

        private sealed class AllergyHitDto
        {
            public int IngredientIndex { get; set; }
            public string IngredientName { get; set; } = "";
            public string MatchedKeyword { get; set; } = "";
        }

        private static bool AllergyRequestEnabled(HttpRequest req)
        {
            var q = req?.Query["allergy"].ToString() ?? "";
            if (string.IsNullOrWhiteSpace(q)) return true;

            return !(q.Equals("off", StringComparison.OrdinalIgnoreCase)
                  || q.Equals("false", StringComparison.OrdinalIgnoreCase)
                  || q.Equals("0", StringComparison.OrdinalIgnoreCase));
        }

        private static List<string> ExtractIngredientNamesInOrder(Recipe recipe)
        {
            if (recipe?.Ingredients == null || recipe.Ingredients.Count == 0) return new();
            return recipe.Ingredients
                .Select(i => (i.GlobalIngredient?.Name ?? i.Name ?? "").Trim())
                .ToList();
        }

        private List<string> ExpandViewerKeywords(User viewer, string minSeverityOrEmpty)
        {
            var userKeywords = viewer.AllergyKeywords ?? new List<string>();

            try
            {
                if (!string.IsNullOrWhiteSpace(minSeverityOrEmpty))
                {
                    var withSeverity = _allergenResolver.ExpandWithSeverity(userKeywords);
                    var filtered = _allergenResolver.FilterByMinSeverity(withSeverity, minSeverityOrEmpty);
                    if (filtered != null && filtered.Count > 0)
                        return filtered.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                }

                var expanded = _allergenResolver.ExpandKeywords(userKeywords);
                if (expanded != null && expanded.Count > 0)
                    return expanded.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch
            {
                // Fall through to raw keywords if resolver throws
            }

            return userKeywords
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<AllergyHitDto> ComputeHitsFromKeywords(IReadOnlyList<string> keywords, List<string> ingredientNames)
        {
            var hits = new List<AllergyHitDto>();
            if (keywords == null || keywords.Count == 0 || ingredientNames.Count == 0) return hits;

            var keys = keywords
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim().ToLowerInvariant())
                .Distinct()
                .ToArray();

            for (int i = 0; i < ingredientNames.Count; i++)
            {
                var name = ingredientNames[i];
                if (string.IsNullOrWhiteSpace(name)) continue;

                var lower = name.ToLowerInvariant();
                foreach (var kw in keys)
                {
                    if (lower.Contains(kw))
                    {
                        hits.Add(new AllergyHitDto
                        {
                            IngredientIndex = i,
                            IngredientName = name,
                            MatchedKeyword = kw
                        });
                        break;
                    }
                }
            }
            return hits;
        }

        // ---------------------- Create ----------------------

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> CreateRecipe([FromBody] CreateRecipeDto dto)
        {
            if (!User.TryGetUserId(out var userGuid))
                return Unauthorized();

            // Validate ingredients against prohibited list (haram, dangerous, etc.)
            var ingredientNames = dto.Ingredients.Select(i => i.Name).ToList();
            var validationResult = await _ingredientValidation.ValidateIngredientsAsync(ingredientNames);
            if (!validationResult.IsValid)
            {
                return BadRequest(new
                {
                    message = "Recipe contains prohibited ingredients",
                    violations = validationResult.Violations.Select(v => new
                    {
                        ingredient = v.IngredientName,
                        category = v.Category,
                        reason = v.Reason
                    })
                });
            }

            var normalizedIngredients = new List<Ingredient>();

            foreach (var i in dto.Ingredients)
            {
                var trimmedName = i.Name.Trim().ToLower();

                var existingGlobalIngredient = await _context.GlobalIngredients
                    .FirstOrDefaultAsync(g => g.Name.ToLower() == trimmedName);

                if (existingGlobalIngredient == null)
                {
                    existingGlobalIngredient = new GlobalIngredient { Name = i.Name.Trim() };
                    _context.GlobalIngredients.Add(existingGlobalIngredient);
                    await _context.SaveChangesAsync(); // get Id
                }

                normalizedIngredients.Add(new Ingredient
                {
                    Quantity = i.Quantity,
                    GlobalIngredientId = existingGlobalIngredient.Id
                });
            }

            var recipe = new Recipe
            {
                Title = dto.Title,
                Description = dto.Description,
                Instructions = dto.Instructions,
                Region = dto.Region,
                ImageUrl = dto.ImageUrl,
                ImageBlobName = dto.ImageBlobName,
                UserId = userGuid,
                CreatedAt = DateTime.UtcNow,
                PrepTimeMinutes = dto.PrepTimeMinutes,
                CookTimeMinutes = dto.CookTimeMinutes,
                Difficulty = DifficultyExtensions.ParseDifficultyOrNull(dto.Difficulty),
                Type = DifficultyExtensions.ParseRecipeTypeOrNull(dto.Type) ?? RecipeType.Meal,
                Ingredients = normalizedIngredients,
                RecipeTags = new List<RecipeTag>()
            };

            var incomingTagNames = NormalizeNames(dto.Tags ?? new List<string>());
            var tagLookup = await EnsureTagsExistAsync(incomingTagNames);
            ReplaceTagsWithNames(recipe, incomingTagNames, tagLookup);

            // Generate SEO-friendly slug from title
            var baseSlug = SlugHelper.GenerateSlug(dto.Title);
            var existingSlugs = await _context.Recipes
                .Where(r => r.UserId == userGuid && r.Slug != null)
                .Select(r => r.Slug!)
                .ToListAsync();
            recipe.Slug = SlugHelper.MakeUnique(baseSlug, s => existingSlugs.Contains(s));

            _context.Recipes.Add(recipe);
            await _context.SaveChangesAsync();

            // Log ingredient usage for B2B analytics (fire-and-forget)
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = HttpContext.Request.Headers["User-Agent"].ToString();
            _ = _ingredientUsage.LogRecipeIngredientsAsync(
                recipe.Id,
                userGuid,
                normalizedIngredients,
                ipAddress,
                userAgent,
                CancellationToken.None);

            // Get username for the URL
            var user = await _context.Users.FindAsync(userGuid);
            var username = user?.Username ?? userGuid.ToString();

            return Ok(new
            {
                recipe.Id,
                recipe.Title,
                recipe.Slug,
                recipe.Description,
                recipe.Instructions,
                recipe.Region,
                Tags = string.Join(",", ToTagNameArray(recipe)), // legacy
                tagNames = ToTagNameArray(recipe),               // NEW
                recipe.PrepTimeMinutes,
                recipe.CookTimeMinutes,
                Difficulty = recipe.Difficulty.ToDisplayString(),
                recipe.ImageUrl,
                recipe.CreatedAt,
                AuthorUsername = username,
                // SEO-friendly URL path: /@username/slug
                UrlPath = $"/@{username}/{recipe.Slug}",
                // echo new ingredients with IDs (no allergen calc on create)
                Ingredients = recipe.Ingredients.Select(i => new IngredientDto
                {
                    IngredientId = i.Id,
                    GlobalIngredientId = i.GlobalIngredientId,
                    Name = i.GlobalIngredient != null ? i.GlobalIngredient.Name : (i.Name ?? ""),
                    Quantity = i.Quantity ?? "",
                    IsAllergen = null,
                    AllergySeverity = null
                }).ToList()
            });
        }

        // Upload one-off image (frontend helper)
        [Authorize]
        [HttpPost("upload-recipe-image")]
        public async Task<IActionResult> UploadRecipeImage(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            if (!User.TryGetUserId(out var userId))
                return Unauthorized();

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound("User not found.");

            var fileName = $"{user.Username}_recipe_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var imageUrl = await _blobService.UploadRecipeImageAsync(file, fileName);

            return Ok(new { imageUrl, imageBlobName = fileName });
        }

        // ---------------------- Read ----------------------

        [AllowAnonymous]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetRecipeById(Guid id)
        {
            var recipe = await _context.Recipes
                .Include(r => r.User)
                .Include(r => r.Ingredients).ThenInclude(i => i.GlobalIngredient)
                .Include(r => r.RecipeTags).ThenInclude(rt => rt.Tag)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (recipe == null)
                return NotFound();

            // -------- ordered ingredient names (original casing) --------
            var ingredientNames = recipe.Ingredients
                .Select(i => (i.GlobalIngredient != null ? i.GlobalIngredient.Name : i.Name) ?? string.Empty)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();

            var tagNames = ToTagNameArray(recipe);

            var authorUsername = recipe.User?.Username ?? "Unknown";
            var recipeDto = new RecipeDto
            {
                Id = recipe.Id,
                Title = recipe.Title,
                Slug = recipe.Slug,
                UrlPath = !string.IsNullOrEmpty(recipe.Slug) ? $"/@{authorUsername}/{recipe.Slug}" : null,
                Description = recipe.Description,
                Instructions = recipe.Instructions,
                Region = recipe.Region,
                ImageUrl = recipe.ImageUrl,
                PhotoCredit = recipe.PhotoCredit,
                PhotoCreditUrl = recipe.PhotoCreditUrl,
                CreatedAt = recipe.CreatedAt,
                AuthorUsername = authorUsername,

                Ingredients = new List<IngredientDto>(),

                Tags = tagNames.ToList(),

                IngredientNames = ingredientNames,

                PrepTimeMinutes = recipe.PrepTimeMinutes,
                CookTimeMinutes = recipe.CookTimeMinutes,
                Difficulty = recipe.Difficulty.ToDisplayString(),
                Type = recipe.Type.ToDisplayString(),

                MatchedAllergenTerms = new List<string>(),
                MatchedAllergenSeverity = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            };

            // ---------------- helpers ----------------
            static string Norm(string s) =>
                Regex.Replace((s ?? "").Trim().ToLowerInvariant(), @"\s+", " ");

            static string Key(string s) => Norm(s);

            var ingLower = ingredientNames
                .Select(Norm)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();

            // IMPORTANT: default allergyMin to "low" (otherwise some implementations return nothing)
            var minSeverityQueryRaw = HttpContext.Request.Query["allergyMin"].ToString();
            var minSeverityQuery = string.IsNullOrWhiteSpace(minSeverityQueryRaw) ? "low" : minSeverityQueryRaw.Trim();

            AllergenResolver.Severity Threshold(string input) =>
                (input ?? "").Trim().ToLowerInvariant() switch
                {
                    "high" => AllergenResolver.Severity.High,
                    "medium" => AllergenResolver.Severity.Medium,
                    "low" => AllergenResolver.Severity.Low,
                    _ => AllergenResolver.Severity.Low
                };

            // -------- legacy highlight payload (kept for compatibility) --------
            object allergyPayload = new
            {
                enabled = false,
                keywords = Array.Empty<string>(),
                hits = Array.Empty<object>()
            };

            // -------- load viewer allergy ROOT terms (canonical: Milk/Gluten/Nickel etc.) --------
            List<string> userRootKeywords = new();

            // Optional DEV override: ?allergies=nickel,peanut
            var overrideParam = HttpContext.Request.Query["allergies"].ToString();
            if (!string.IsNullOrWhiteSpace(overrideParam))
            {
                userRootKeywords = overrideParam
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            else if (AllergyRequestEnabled(HttpContext.Request) && User.Identity?.IsAuthenticated == true)
            {
                var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (Guid.TryParse(uid, out var viewerId))
                {
                    userRootKeywords = await _context.UserAllergies
                        .Where(ua => ua.UserId == viewerId)
                        .Select(ua => ua.Allergy.Name) // canonical bucket names
                        .ToListAsync();

                    userRootKeywords = userRootKeywords
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            }

            // ---------------- compute highlight + severity maps from server ----------------
            try
            {
                if (userRootKeywords.Count > 0 && ingLower.Count > 0)
                {
                    // normalize roots so resolver matching is consistent (Milk == milk, etc.)
                    var normalizedRoots = userRootKeywords
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => Regex.Replace(x.Trim(), @"\s+", " "))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var minSev = Threshold(minSeverityQuery);

                    // 1) severity-aware expansion
                    var expandedWithSevForHits = _allergenResolver.ExpandWithSeverity(normalizedRoots);

                    // 2) terms list (strings) filtered by min severity
                    var filteredForHits = _allergenResolver.FilterByMinSeverity(expandedWithSevForHits, minSeverityQuery);

                    // 3) if filter produced nothing, fall back to non-severity expansion
                    var expandedForHits = (filteredForHits != null && filteredForHits.Count > 0)
     ? filteredForHits
     : (_allergenResolver.ExpandKeywords(normalizedRoots)?.ToList() ?? new List<string>());


                    expandedForHits = expandedForHits
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    // legacy "hits" (substring match)
                    var hits = ComputeHitsFromKeywords(expandedForHits, ingredientNames);
                    var hitsDto = hits.Select(h => new
                    {
                        h.IngredientIndex,
                        h.IngredientName,
                        h.MatchedKeyword
                    }).ToList();

                    allergyPayload = new
                    {
                        enabled = expandedForHits.Count > 0 && hitsDto.Count > 0,
                        keywords = expandedForHits,
                        hits = hitsDto
                    };

                    // build max severity map per ingredient (normalized ingredient key)
                    var filteredTermsWithSev = expandedWithSevForHits
                        .Where(t => t.severity >= minSev)
                        .ToList();

                    var termMaxSev = new Dictionary<string, AllergenResolver.Severity>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (term, sev) in filteredTermsWithSev)
                    {
                        var k = Norm(term);
                        if (string.IsNullOrWhiteSpace(k)) continue;

                        if (!termMaxSev.TryGetValue(k, out var cur) || sev > cur)
                            termMaxSev[k] = sev;
                    }

                    var sevPerIngredient = new Dictionary<string, AllergenResolver.Severity>(StringComparer.OrdinalIgnoreCase);
                    var matchedTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var ing in ingLower)
                    {
                        foreach (var kv in termMaxSev)
                        {
                            var term = kv.Key; // normalized
                            var sev = kv.Value;

                            if (ing.Equals(term, StringComparison.OrdinalIgnoreCase) ||
                                ing.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                term.Contains(ing, StringComparison.OrdinalIgnoreCase))
                            {
                                if (!sevPerIngredient.TryGetValue(ing, out var cur) || sev > cur)
                                    sevPerIngredient[ing] = sev;

                                matchedTerms.Add(term);
                            }
                        }
                    }

                    static string SevToString(AllergenResolver.Severity s) =>
                        s == AllergenResolver.Severity.High ? "high" :
                        s == AllergenResolver.Severity.Medium ? "medium" : "low";

                    recipeDto.MatchedAllergenSeverity = sevPerIngredient
                        .ToDictionary(kv => kv.Key, kv => SevToString(kv.Value), StringComparer.OrdinalIgnoreCase);

                    recipeDto.MatchedAllergenTerms = matchedTerms.ToList();
                }
                else
                {
                    recipeDto.MatchedAllergenTerms = new List<string>();
                    recipeDto.MatchedAllergenSeverity = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch
            {
                recipeDto.MatchedAllergenTerms = new List<string>();
                recipeDto.MatchedAllergenSeverity = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            // -------- attach per-row ingredient flags & IDs --------
            var ingredientsWithFlags = recipe.Ingredients.Select(i =>
            {
                var resolvedName = i.GlobalIngredient != null ? i.GlobalIngredient.Name : (i.Name ?? "");
                var k = Key(resolvedName);
                recipeDto.MatchedAllergenSeverity.TryGetValue(k, out var sev);

                return new IngredientDto
                {
                    IngredientId = i.Id,
                    GlobalIngredientId = i.GlobalIngredientId,
                    Name = resolvedName,
                    Quantity = i.Quantity ?? "",
                    IsAllergen = sev != null,
                    AllergySeverity = sev // "high" | "medium" | "low" | null
                };
            }).ToList();

            recipeDto.Ingredients = ingredientsWithFlags;

            // ---------------- Remix meta (unchanged) ----------------
            var remixLink = await _context.RecipeRemixes
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.RemixRecipeId == id);

            return Ok(new
            {
                recipe = recipeDto,
                tags = string.Join(",", tagNames),
                tagNames,
                allergy = allergyPayload,
                remixMeta = remixLink == null
                    ? null
                    : new
                    {
                        isRemix = true,
                        parentRecipeId = remixLink.ParentRecipeId,
                        originalRecipeId = remixLink.OriginalRecipeId
                    }
            });
        }

        /// <summary>
        /// Get recipe by username and slug for SEO-friendly URLs.
        /// GET /api/recipe/by-slug/{username}/{slug}
        /// Example: /api/recipe/by-slug/johndoe/chocolate-chip-cookies
        /// </summary>
        [AllowAnonymous]
        [HttpGet("by-slug/{username}/{slug}")]
        public async Task<IActionResult> GetRecipeBySlug(string username, string slug)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(slug))
                return BadRequest("Username and slug are required.");

            // Find the user by username
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username != null &&
                    u.Username.ToLower() == username.ToLower());

            if (user == null)
                return NotFound("User not found.");

            // Find the recipe by user and slug
            var recipe = await _context.Recipes
                .Include(r => r.User)
                .Include(r => r.Ingredients).ThenInclude(i => i.GlobalIngredient)
                .Include(r => r.RecipeTags).ThenInclude(rt => rt.Tag)
                .FirstOrDefaultAsync(r => r.UserId == user.Id &&
                    r.Slug != null && r.Slug.ToLower() == slug.ToLower() &&
                    !r.IsDeleted);

            if (recipe == null)
                return NotFound("Recipe not found.");

            // Redirect to the standard endpoint to reuse all the allergy logic
            return await GetRecipeById(recipe.Id);
        }

        // ---------------------- Tag-filtered list ----------------------

        /// <summary>
        /// Filter recipes by tags.
        /// GET /api/recipe?tags=vegan,halal&mode=any|all&page=1&pageSize=20
        /// </summary>
        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> QueryByTags(
            [FromQuery] string? tags = null,
            [FromQuery] string mode = "any",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var tagList = (tags ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            IQueryable<Recipe> q = _context.Recipes
                .AsNoTracking()
                .Include(r => r.User)
                .Include(r => r.RecipeTags).ThenInclude(rt => rt.Tag)
                .Where(r => !r.IsDeleted);

            if (tagList.Count > 0)
            {
                var tagIds = await _context.Tags
                    .Where(t => tagList.Contains(t.Name))
                    .Select(t => t.Id)
                    .ToListAsync();

                if (tagIds.Count == 0)
                    return Ok(new { total = 0, items = Array.Empty<object>(), page, pageSize });

                if (mode.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    q = q.Where(r => tagIds.All(tagId => r.RecipeTags.Any(rt => rt.TagId == tagId)));
                }
                else
                {
                    q = q.Where(r => r.RecipeTags.Any(rt => tagIds.Contains(rt.TagId)));
                }
            }

            q = q.OrderByDescending(r => r.CreatedAt);

            var total = await q.CountAsync();

            var items = await q
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(r => new
                {
                    r.Id,
                    r.Title,
                    r.Description,
                    r.ImageUrl,
                    r.CreatedAt,
                    Username = r.User.Username,
                    // Project tags directly in SQL instead of using ToTagNameArray
                    tagNames = r.RecipeTags
                        .Where(rt => rt.Tag != null && rt.Tag.Name != null)
                        .Select(rt => rt.Tag.Name)
                        .ToList()
                })
                .ToListAsync();

            // Build the response with Tags string (done in memory after query)
            var result = items.Select(r => new
            {
                r.Id,
                r.Title,
                r.Description,
                r.ImageUrl,
                r.CreatedAt,
                r.Username,
                Tags = string.Join(",", r.tagNames),
                r.tagNames
            }).ToList();

            return Ok(new { total, items = result, page, pageSize });
        }

        // ---------------------- Update ----------------------

        // A) Multipart/form-data update (with optional image)
        [Authorize]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromForm] UpdateRecipeDto dto)
        {
            var userId = CurrentUserId();
            if (userId is null) return Unauthorized();

            var recipe = await _context.Recipes
                .IgnoreQueryFilters()
                .Include(r => r.Ingredients).ThenInclude(i => i.GlobalIngredient) // include ingredients for return payload
                .Include(r => r.RecipeTags).ThenInclude(rt => rt.Tag)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (recipe is null) return NotFound();
            if (recipe.UserId != userId) return Forbid();
            if (recipe.IsDeleted) return StatusCode(StatusCodes.Status410Gone, "This recipe is unavailable.");

            if (dto.Title != null) recipe.Title = dto.Title;
            if (dto.Description != null) recipe.Description = dto.Description;
            if (dto.Instructions != null) recipe.Instructions = dto.Instructions;
            if (dto.Region != null) recipe.Region = dto.Region;

            if (dto.Tags != null)
            {
                var names = NormalizeNames(dto.Tags);
                var lookup = await EnsureTagsExistAsync(names);
                ReplaceTagsWithNames(recipe, names, lookup);
            }
            else if (Request?.Form.TryGetValue("Tags", out var csv) == true && !string.IsNullOrWhiteSpace(csv))
            {
                var names = csv.ToString()
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
                var lookup = await EnsureTagsExistAsync(names);
                ReplaceTagsWithNames(recipe, names, lookup);
            }

            if (dto.PrepTimeMinutes.HasValue) recipe.PrepTimeMinutes = dto.PrepTimeMinutes.Value;
            if (dto.CookTimeMinutes.HasValue) recipe.CookTimeMinutes = dto.CookTimeMinutes.Value;
            if (dto.Difficulty != null) recipe.Difficulty = DifficultyExtensions.ParseDifficultyOrNull(dto.Difficulty);
            if (dto.Type != null)
            {
                var parsedType = DifficultyExtensions.ParseRecipeTypeOrNull(dto.Type);
                if (parsedType.HasValue) recipe.Type = parsedType.Value;
            }

            string? oldBlob = recipe.ImageBlobName;

            if (dto.Image is not null && dto.Image.Length > 0)
            {
                var newBlob = $"recipe_{recipe.Id}_{Guid.NewGuid()}{Path.GetExtension(dto.Image.FileName)}";
                string newUrl;

                try
                {
                    newUrl = await _blobService.UploadRecipeImageAsync(dto.Image, newBlob);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Recipe image upload failed for recipe {RecipeId}", recipe.Id);
                    return StatusCode(StatusCodes.Status500InternalServerError, "Image upload failed.");
                }

                recipe.ImageBlobName = newBlob;
                recipe.ImageUrl = newUrl;
            }

            recipe.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict("This recipe was updated by someone else. Please refresh and try again.");
            }

            if (!string.IsNullOrWhiteSpace(oldBlob) && oldBlob != recipe.ImageBlobName)
            {
                try { await _blobService.DeleteRecipeImageIfExistsAsync(oldBlob); }
                catch (Exception ex) { _log.LogWarning(ex, "Failed deleting old recipe blob {Blob}", oldBlob); }
            }

            return Ok(new
            {
                recipe.Id,
                recipe.Title,
                recipe.Description,
                recipe.Instructions,
                recipe.Region,
                Tags = string.Join(",", ToTagNameArray(recipe)), // legacy
                tagNames = ToTagNameArray(recipe),               // NEW
                recipe.PrepTimeMinutes,
                recipe.CookTimeMinutes,
                Difficulty = recipe.Difficulty.ToDisplayString(),
                recipe.ImageUrl,
                recipe.UpdatedAt,
                Ingredients = recipe.Ingredients.Select(i => new IngredientDto
                {
                    IngredientId = i.Id,
                    GlobalIngredientId = i.GlobalIngredientId,
                    Name = i.GlobalIngredient != null ? i.GlobalIngredient.Name : (i.Name ?? ""),
                    Quantity = i.Quantity ?? "",
                    IsAllergen = null,
                    AllergySeverity = null
                }).ToList()
            });
        }

        // B) JSON update (text-only edits; optional full ingredient replace)
        [Authorize]
        [HttpPut("{id:guid}/json")]
        [Consumes("application/json")]
        public async Task<IActionResult> UpdateJson(Guid id, [FromBody] UpdateRecipeJsonDto dto)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

            await using var tx = await _context.Database.BeginTransactionAsync();

            var recipe = await _context.Recipes
                .IgnoreQueryFilters()
                .Include(r => r.Ingredients).ThenInclude(i => i.GlobalIngredient)
                .Include(r => r.RecipeTags).ThenInclude(rt => rt.Tag)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (recipe is null) return NotFound();
            if (recipe.UserId != userId) return Forbid();
            if (recipe.IsDeleted) return StatusCode(StatusCodes.Status410Gone, "This recipe is unavailable.");

            try
            {
                if (dto.Title != null) recipe.Title = dto.Title;
                if (dto.Description != null) recipe.Description = dto.Description;
                if (dto.Instructions != null) recipe.Instructions = dto.Instructions;
                if (dto.Region != null) recipe.Region = dto.Region;
                if (dto.PrepTimeMinutes.HasValue) recipe.PrepTimeMinutes = dto.PrepTimeMinutes.Value;
                if (dto.CookTimeMinutes.HasValue) recipe.CookTimeMinutes = dto.CookTimeMinutes.Value;
                if (dto.Difficulty != null) recipe.Difficulty = DifficultyExtensions.ParseDifficultyOrNull(dto.Difficulty);
                if (dto.Type != null)
                {
                    var parsedType = DifficultyExtensions.ParseRecipeTypeOrNull(dto.Type);
                    if (parsedType.HasValue) recipe.Type = parsedType.Value;
                }

                if (dto.Tags != null)
                {
                    var names = NormalizeNames(dto.Tags);
                    var lookup = await EnsureTagsExistAsync(names);
                    ReplaceTagsWithNames(recipe, names, lookup);
                }

                if (dto.Ingredients != null)
                {
                    // Validate ingredients against prohibited list (haram, dangerous, etc.)
                    var ingredientNames = dto.Ingredients
                        .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                        .Select(i => i.Name!)
                        .ToList();
                    var validationResult = await _ingredientValidation.ValidateIngredientsAsync(ingredientNames);
                    if (!validationResult.IsValid)
                    {
                        await tx.RollbackAsync();
                        return BadRequest(new
                        {
                            message = "Recipe contains prohibited ingredients",
                            violations = validationResult.Violations.Select(v => new
                            {
                                ingredient = v.IngredientName,
                                category = v.Category,
                                reason = v.Reason
                            })
                        });
                    }

                    await _context.Ingredients
                        .Where(i => i.RecipeId == id)
                        .ExecuteDeleteAsync();

                    var newChildren = new List<Ingredient>();

                    foreach (var i in dto.Ingredients)
                    {
                        var trimmed = (i.Name ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(trimmed)) continue;

                        var lower = trimmed.ToLower();
                        var global = await _context.GlobalIngredients
                            .FirstOrDefaultAsync(g => g.Name.ToLower() == lower);

                        if (global == null)
                        {
                            global = new GlobalIngredient { Name = trimmed };
                            _context.GlobalIngredients.Add(global);
                            await _context.SaveChangesAsync();
                        }

                        newChildren.Add(new Ingredient
                        {
                            RecipeId = id,
                            GlobalIngredientId = global.Id,
                            Quantity = i.Quantity ?? ""
                        });
                    }

                    if (newChildren.Count > 0)
                        await _context.Ingredients.AddRangeAsync(newChildren);
                }

                recipe.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                // reload ingredients (they now have fresh Ids)
                var updatedIngredients = await _context.Ingredients
                    .Where(i => i.RecipeId == id)
                    .Include(i => i.GlobalIngredient)
                    .ToListAsync();

                return Ok(new
                {
                    recipe.Id,
                    recipe.Title,
                    recipe.Description,
                    recipe.Instructions,
                    recipe.Region,
                    Tags = string.Join(",", ToTagNameArray(recipe)), // legacy
                    tagNames = ToTagNameArray(recipe),               // NEW
                    recipe.PrepTimeMinutes,
                    recipe.CookTimeMinutes,
                    Difficulty = recipe.Difficulty.ToDisplayString(),
                    recipe.ImageUrl,
                    recipe.UpdatedAt,
                    Ingredients = updatedIngredients.Select(i => new IngredientDto
                    {
                        IngredientId = i.Id,
                        GlobalIngredientId = i.GlobalIngredientId,
                        Name = i.GlobalIngredient != null ? i.GlobalIngredient.Name : (i.Name ?? ""),
                        Quantity = i.Quantity ?? "",
                        IsAllergen = null,
                        AllergySeverity = null
                    }).ToList()
                });
            }
            catch (DbUpdateConcurrencyException ex)
            {
                var entities = string.Join(", ", ex.Entries.Select(e => e.Entity.GetType().Name));
                _log.LogWarning(ex, "Concurrency conflict updating recipe {RecipeId}. Entities: {Entities}", id, entities);
                await tx.RollbackAsync();
                return Conflict("This recipe was updated by someone else. Please refresh and try again.");
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // ---------------------- Delete (soft) ----------------------

        [Authorize]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> SoftDelete(Guid id)
        {
            var userId = CurrentUserId();
            if (userId is null) return Unauthorized();

            var recipe = await _context.Recipes
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.Id == id);

            if (recipe is null) return NotFound();
            if (recipe.UserId != userId) return Forbid();

            if (recipe.IsDeleted) return NoContent();

            recipe.IsDeleted = true;
            recipe.DeletedAt = DateTime.UtcNow;
            recipe.DeletedByUserId = userId;
            recipe.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // ---------------------- Likes / Comments / Remix / Remixes ----------------------

        [Authorize]
        [HttpPost("{id}/like")]
        public async Task<IActionResult> LikeRecipe(Guid id)
        {
            if (!User.TryGetUserId(out var userGuid))
                return Unauthorized();

            var recipe = await _context.Recipes.FindAsync(id);
            if (recipe == null) return NotFound("Recipe not found.");

            var alreadyLiked = await _context.RecipeLikes
                .AnyAsync(rl => rl.RecipeId == id && rl.UserId == userGuid);

            if (alreadyLiked)
                return BadRequest("You already liked this recipe.");

            var like = new RecipeLike
            {
                RecipeId = id,
                UserId = userGuid,
                LikedAt = DateTime.UtcNow
            };

            _context.RecipeLikes.Add(like);
            await _context.SaveChangesAsync();

            // Notify recipe owner
            await _notificationService.NotifyRecipeLikeAsync(recipe.UserId, userGuid, id);

            return Ok("Recipe liked.");
        }

        [Authorize]
        [HttpDelete("{id}/like")]
        public async Task<IActionResult> UnlikeRecipe(Guid id)
        {
            if (!User.TryGetUserId(out var userGuid))
                return Unauthorized();

            var like = await _context.RecipeLikes
                .FirstOrDefaultAsync(rl => rl.RecipeId == id && rl.UserId == userGuid);

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
            if (!User.TryGetUserId(out var userGuid))
                return Unauthorized();

            var recipe = await _context.Recipes.FindAsync(recipeId);
            if (recipe == null) return NotFound("Recipe not found.");

            var comment = new Comment
            {
                Content = dto.Content,
                CreatedAt = DateTime.UtcNow,
                RecipeId = recipeId,
                UserId = userGuid
            };

            _context.Comments.Add(comment);
            await _context.SaveChangesAsync();

            // Notify recipe owner
            await _notificationService.NotifyRecipeCommentAsync(recipe.UserId, userGuid, recipeId, comment.Id);

            // Parse and notify @mentions
            var mentions = ParseMentions(dto.Content);
            if (mentions.Any())
            {
                var mentionedUsers = await _context.Users
                    .Where(u => mentions.Contains(u.Username.ToLower()))
                    .Select(u => u.Id)
                    .ToListAsync();
                await _notificationService.NotifyMentionsAsync(mentionedUsers, userGuid, recipeId, comment.Id);
            }

            // Fetch user info to return complete CommentDto
            var user = await _context.Users.FindAsync(userGuid);

            return Ok(new CommentDto
            {
                Id = comment.Id,
                Content = comment.Content,
                CreatedAt = comment.CreatedAt,
                UserId = comment.UserId,
                Username = user?.Username,
                DisplayName = user?.DisplayName,
                ProfileImageUrl = user?.ProfileImageUrl,
                ParentCommentId = null,
                LikesCount = 0,
                IsLiked = false,
                RepliesCount = 0,
                Replies = null
            });
        }

        /// <summary>
        /// Parse @mentions from text content
        /// </summary>
        private static List<string> ParseMentions(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return new List<string>();
            var matches = Regex.Matches(content, @"@(\w+)");
            return matches.Select(m => m.Groups[1].Value.ToLower()).Distinct().ToList();
        }

        [AllowAnonymous]
        [HttpGet("{recipeId}/comments")]
        public async Task<ActionResult<IEnumerable<CommentDto>>> GetComments(Guid recipeId)
        {
            Guid? currentUserId = User.GetUserIdOrNull();

            // Fetch all comments for this recipe in one query (simpler, more reliable)
            var allComments = await _context.Comments
                .Where(c => c.RecipeId == recipeId)
                .Include(c => c.User)
                .Include(c => c.Likes)
                .OrderBy(c => c.CreatedAt)
                .ToListAsync();

            // Build a lookup of parent -> children
            var repliesByParent = allComments
                .Where(c => c.ParentCommentId != null)
                .GroupBy(c => c.ParentCommentId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Map top-level comments to DTOs with nested replies
            var topLevelComments = allComments
                .Where(c => c.ParentCommentId == null)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => ToCommentDto(c, currentUserId, repliesByParent))
                .ToList();

            return Ok(topLevelComments);
        }

        private CommentDto ToCommentDto(Comment c, Guid? currentUserId, Dictionary<Guid, List<Comment>>? repliesByParent)
        {
            var replies = repliesByParent != null && repliesByParent.TryGetValue(c.Id, out var childList)
                ? childList.OrderBy(r => r.CreatedAt).Select(r => ToCommentDto(r, currentUserId, null)).ToList()
                : new List<CommentDto>();

            return new CommentDto
            {
                Id = c.Id,
                Content = c.Content,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                UserId = c.UserId,
                Username = c.User?.Username,
                DisplayName = c.User?.DisplayName,
                ProfileImageUrl = c.User?.ProfileImageUrl,
                ParentCommentId = c.ParentCommentId,
                LikesCount = c.Likes?.Count ?? 0,
                IsLiked = currentUserId.HasValue && (c.Likes?.Any(l => l.UserId == currentUserId.Value) ?? false),
                RepliesCount = replies.Count,
                Replies = replies.Count > 0 ? replies : null
            };
        }

        [Authorize]
        [HttpPut("{recipeId}/comments/{commentId}")]
        public async Task<IActionResult> UpdateComment(Guid recipeId, Guid commentId, [FromBody] UpdateCommentDto dto)
        {
            if (!User.TryGetUserId(out var userGuid))
                return Unauthorized();

            var comment = await _context.Comments
                .FirstOrDefaultAsync(c => c.Id == commentId && c.RecipeId == recipeId);

            if (comment == null) return NotFound("Comment not found.");

            // Only comment owner can edit
            if (comment.UserId != userGuid)
                return Forbid();

            comment.Content = dto.Content;
            comment.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new { comment.Id, comment.Content, comment.UpdatedAt });
        }

        [Authorize]
        [HttpDelete("{recipeId}/comments/{commentId}")]
        public async Task<IActionResult> DeleteComment(Guid recipeId, Guid commentId)
        {
            if (!User.TryGetUserId(out var userGuid))
                return Unauthorized();

            var comment = await _context.Comments
                .Include(c => c.Recipe)
                .FirstOrDefaultAsync(c => c.Id == commentId && c.RecipeId == recipeId);

            if (comment == null) return NotFound("Comment not found.");

            // Comment owner or recipe owner can delete
            if (comment.UserId != userGuid && comment.Recipe?.UserId != userGuid)
                return Forbid();

            // Delete all replies first
            var replies = await _context.Comments.Where(c => c.ParentCommentId == commentId).ToListAsync();
            _context.Comments.RemoveRange(replies);

            _context.Comments.Remove(comment);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [Authorize]
        [HttpPost("{recipeId}/comments/{commentId}/like")]
        public async Task<IActionResult> LikeComment(Guid recipeId, Guid commentId)
        {
            if (!User.TryGetUserId(out var userGuid))
                return Unauthorized();

            var comment = await _context.Comments.FirstOrDefaultAsync(c => c.Id == commentId && c.RecipeId == recipeId);
            if (comment == null) return NotFound("Comment not found.");

            var existingLike = await _context.CommentLikes.FindAsync(userGuid, commentId);
            if (existingLike != null)
                return BadRequest("Already liked this comment.");

            var like = new CommentLike
            {
                UserId = userGuid,
                CommentId = commentId
            };

            _context.CommentLikes.Add(like);
            await _context.SaveChangesAsync();

            var likesCount = await _context.CommentLikes.CountAsync(l => l.CommentId == commentId);
            return Ok(new { liked = true, likesCount });
        }

        [Authorize]
        [HttpDelete("{recipeId}/comments/{commentId}/like")]
        public async Task<IActionResult> UnlikeComment(Guid recipeId, Guid commentId)
        {
            if (!User.TryGetUserId(out var userGuid))
                return Unauthorized();

            var like = await _context.CommentLikes.FindAsync(userGuid, commentId);
            if (like == null)
                return NotFound("You haven't liked this comment.");

            _context.CommentLikes.Remove(like);
            await _context.SaveChangesAsync();

            var likesCount = await _context.CommentLikes.CountAsync(l => l.CommentId == commentId);
            return Ok(new { liked = false, likesCount });
        }

        [Authorize]
        [HttpPost("{recipeId}/comments/{commentId}/replies")]
        public async Task<IActionResult> AddReply(Guid recipeId, Guid commentId, [FromBody] CreateReplyDto dto)
        {
            if (!User.TryGetUserId(out var userGuid))
                return Unauthorized();

            var parentComment = await _context.Comments
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == commentId && c.RecipeId == recipeId);

            if (parentComment == null) return NotFound("Parent comment not found.");

            // Only allow 1 level of nesting (replies to top-level comments only)
            if (parentComment.ParentCommentId != null)
                return BadRequest("Cannot reply to a reply. Please reply to the original comment.");

            var reply = new Comment
            {
                Content = dto.Content,
                RecipeId = recipeId,
                UserId = userGuid,
                ParentCommentId = commentId
            };

            _context.Comments.Add(reply);
            await _context.SaveChangesAsync();

            // Notify the parent comment owner
            await _notificationService.NotifyCommentReplyAsync(parentComment.UserId, userGuid, recipeId, reply.Id);

            // Parse and notify @mentions
            var mentions = ParseMentions(dto.Content);
            if (mentions.Any())
            {
                var mentionedUsers = await _context.Users
                    .Where(u => mentions.Contains(u.Username.ToLower()))
                    .Select(u => u.Id)
                    .ToListAsync();
                await _notificationService.NotifyMentionsAsync(mentionedUsers, userGuid, recipeId, reply.Id);
            }

            var user = await _context.Users.FindAsync(userGuid);

            return Ok(new CommentDto
            {
                Id = reply.Id,
                Content = reply.Content,
                CreatedAt = reply.CreatedAt,
                UserId = reply.UserId,
                Username = user?.Username,
                DisplayName = user?.DisplayName,
                ProfileImageUrl = user?.ProfileImageUrl,
                ParentCommentId = reply.ParentCommentId,
                LikesCount = 0,
                IsLiked = false,
                RepliesCount = 0,
                Replies = null
            });
        }

        [Authorize]
        [HttpPost("{id}/repost")]
        public async Task<IActionResult> RepostRecipe(Guid id)
        {
            if (!User.TryGetUserId(out var userGuid))
                return Unauthorized();

            var existingRecipe = await _context.Recipes.FindAsync(id);
            if (existingRecipe == null)
                return NotFound("Recipe not found.");

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

            // Notify recipe owner
            await _notificationService.NotifyRepostAsync(existingRecipe.UserId, userGuid, id);

            return Ok("Recipe reposted successfully.");
        }

        [Authorize]
        [HttpDelete("{id}/repost")]
        public async Task<IActionResult> UnrepostRecipe(Guid id)
        {
            if (!User.TryGetUserId(out var userGuid))
                return Unauthorized();

            var repost = await _context.RecipeReposts
                .FirstOrDefaultAsync(r => r.UserId == userGuid && r.RecipeId == id);

            if (repost == null)
                return NotFound("You have not reposted this recipe.");

            _context.RecipeReposts.Remove(repost);
            await _context.SaveChangesAsync();

            return Ok("Repost removed successfully.");
        }

        [Authorize]
        [HttpPost("{originalId:guid}/remix")]
        public async Task<IActionResult> RemixRecipe(Guid originalId, [FromBody] CreateRecipeDto dto)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .Where(kvp => kvp.Value?.Errors?.Count > 0)
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                    );
                return BadRequest(new { message = "Invalid remix payload", errors });
            }

            if (!User.TryGetUserId(out var userId))
                return Unauthorized();

            var originalRecipe = await _context.Recipes.FirstOrDefaultAsync(r => r.Id == originalId);
            if (originalRecipe == null) return NotFound("Original recipe not found.");

            // Validate ingredients against prohibited list (haram, dangerous, etc.)
            var ingredientNames = dto.Ingredients.Select(i => i.Name).ToList();
            var validationResult = await _ingredientValidation.ValidateIngredientsAsync(ingredientNames);
            if (!validationResult.IsValid)
            {
                return BadRequest(new
                {
                    message = "Recipe contains prohibited ingredients",
                    violations = validationResult.Violations.Select(v => new
                    {
                        ingredient = v.IngredientName,
                        category = v.Category,
                        reason = v.Reason
                    })
                });
            }

            var normalizedIngredients = new List<Ingredient>();

            foreach (var i in dto.Ingredients)
            {
                var trimmedNameLower = i.Name.Trim().ToLower();

                var existingGlobalIngredient = await _context.GlobalIngredients
                    .FirstOrDefaultAsync(g => g.Name.ToLower() == trimmedNameLower);

                if (existingGlobalIngredient == null)
                {
                    existingGlobalIngredient = new GlobalIngredient { Name = i.Name.Trim() };
                    _context.GlobalIngredients.Add(existingGlobalIngredient);
                    await _context.SaveChangesAsync(); // get Id
                }

                normalizedIngredients.Add(new Ingredient
                {
                    Quantity = i.Quantity,
                    GlobalIngredientId = existingGlobalIngredient.Id
                });
            }

            var remix = new Recipe
            {
                Title = dto.Title,
                Description = dto.Description,
                Instructions = dto.Instructions,
                Region = dto.Region,
                ImageUrl = dto.ImageUrl,
                ImageBlobName = dto.ImageBlobName,
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                Ingredients = normalizedIngredients,
                PrepTimeMinutes = dto.PrepTimeMinutes,
                CookTimeMinutes = dto.CookTimeMinutes,
                Difficulty = DifficultyExtensions.ParseDifficultyOrNull(dto.Difficulty),
                RecipeTags = new List<RecipeTag>()
            };

            var remixTagNames = NormalizeNames(dto.Tags ?? new List<string>());
            var remixLookup = await EnsureTagsExistAsync(remixTagNames);
            ReplaceTagsWithNames(remix, remixTagNames, remixLookup);

            _context.Recipes.Add(remix);
            await _context.SaveChangesAsync();

            var existingRemix = await _context.RecipeRemixes
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.RemixRecipeId == originalId);

            var rootOriginalId = existingRemix?.OriginalRecipeId ?? originalId;
            var parentId = originalId;

            var remixLink = new RecipeRemix
            {
                RemixRecipeId = remix.Id,
                OriginalRecipeId = rootOriginalId,
                ParentRecipeId = parentId,
                RemixedAt = DateTime.UtcNow
            };

            _context.RecipeRemixes.Add(remixLink);
            await _context.SaveChangesAsync();

            // Log ingredient usage for B2B analytics (fire-and-forget)
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = HttpContext.Request.Headers["User-Agent"].ToString();
            _ = _ingredientUsage.LogRecipeIngredientsAsync(
                remix.Id,
                userId,
                normalizedIngredients,
                ipAddress,
                userAgent,
                CancellationToken.None);

            return Ok(new { remixId = remix.Id });
        }

        [AllowAnonymous]
        [HttpGet("{id}/lineage")]
        public async Task<IActionResult> GetRemixLineage(Guid id)
        {
            // Step 1: Load all remix relationships in ONE query (they're lightweight)
            // Note: AsNoTracking not needed when projecting to non-entity types
            var allRemixLinks = await _context.RecipeRemixes
                .Where(r => r.OriginalRecipeId != null)
                .Select(r => new { r.RemixRecipeId, r.OriginalRecipeId })
                .ToListAsync();

            // Step 2: Build the chain of recipe IDs in memory (no DB queries)
            var remixLookup = allRemixLinks.ToDictionary(r => r.RemixRecipeId, r => r.OriginalRecipeId);
            var recipeIdsInLineage = new List<Guid>();
            var currentRecipeId = id;
            var visited = new HashSet<Guid>(); // Prevent infinite loops

            while (currentRecipeId != Guid.Empty && !visited.Contains(currentRecipeId))
            {
                visited.Add(currentRecipeId);
                recipeIdsInLineage.Insert(0, currentRecipeId);

                if (remixLookup.TryGetValue(currentRecipeId, out var originalId) && originalId.HasValue)
                    currentRecipeId = originalId.Value;
                else
                    break;
            }

            if (recipeIdsInLineage.Count == 0)
                return Ok(new List<RecipeDto>());

            // Step 3: Load ALL recipes in the lineage in ONE query
            var recipes = await _context.Recipes
                .Where(r => recipeIdsInLineage.Contains(r.Id))
                .Include(r => r.Ingredients).ThenInclude(i => i.GlobalIngredient)
                .Include(r => r.User)
                .Include(r => r.RecipeTags).ThenInclude(rt => rt.Tag)
                .AsNoTracking()
                .ToListAsync();

            // Step 4: Build the response in the correct order
            var recipeLookup = recipes.ToDictionary(r => r.Id);
            var lineage = recipeIdsInLineage
                .Where(id => recipeLookup.ContainsKey(id))
                .Select(id =>
                {
                    var recipe = recipeLookup[id];
                    return new RecipeDto
                    {
                        Id = recipe.Id,
                        Title = recipe.Title,
                        Description = recipe.Description,
                        Instructions = recipe.Instructions,
                        Region = recipe.Region,
                        ImageUrl = recipe.ImageUrl,
                        PhotoCredit = recipe.PhotoCredit,
                        PhotoCreditUrl = recipe.PhotoCreditUrl,
                        CreatedAt = recipe.CreatedAt,
                        AuthorUsername = recipe.User?.Username ?? "",
                        Ingredients = recipe.Ingredients.Select(i => new IngredientDto
                        {
                            IngredientId = i.Id,
                            GlobalIngredientId = i.GlobalIngredientId,
                            Name = i.GlobalIngredient != null ? i.GlobalIngredient.Name : (i.Name ?? ""),
                            Quantity = i.Quantity ?? ""
                        }).ToList(),
                        Tags = recipe.RecipeTags
                            .Where(rt => rt.Tag != null && !string.IsNullOrWhiteSpace(rt.Tag.Name))
                            .Select(rt => rt.Tag.Name)
                            .Distinct()
                            .ToList(),
                        PrepTimeMinutes = recipe.PrepTimeMinutes,
                        CookTimeMinutes = recipe.CookTimeMinutes,
                        Difficulty = recipe.Difficulty.ToDisplayString(),
                    };
                })
                .ToList();

            return Ok(lineage);
        }

        [AllowAnonymous]
        [HttpGet("{id}/remixes")]
        public async Task<IActionResult> GetRemixes(Guid id)
        {
            var remixes = await _context.RecipeRemixes
                .Where(r => r.OriginalRecipeId == id || r.ParentRecipeId == id)
                .Include(r => r.RemixRecipe).ThenInclude(rr => rr.User)
                .Include(r => r.RemixRecipe).ThenInclude(rr => rr.Ingredients).ThenInclude(i => i.GlobalIngredient)
                .Include(r => r.RemixRecipe).ThenInclude(rr => rr.RecipeTags).ThenInclude(rt => rt.Tag)
                .AsNoTracking()
                .Select(r => new RemixDto
                {
                    Id = r.RemixRecipe.Id,
                    Title = r.RemixRecipe.Title,
                    Description = r.RemixRecipe.Description,
                    Instructions = r.RemixRecipe.Instructions,
                    Region = r.RemixRecipe.Region,
                    ImageUrl = r.RemixRecipe.ImageUrl,
                    CreatedAt = r.RemixRecipe.CreatedAt,

                    UserId = r.RemixRecipe.UserId,
                    Username = r.RemixRecipe.User.Username,
                    DisplayName = r.RemixRecipe.User.DisplayName,
                    ProfileImageUrl = r.RemixRecipe.User.ProfileImageUrl,

                    Tags = string.Join(",", r.RemixRecipe.RecipeTags.Select(rt => rt.Tag.Name)),
                    PrepTimeMinutes = r.RemixRecipe.PrepTimeMinutes,
                    CookTimeMinutes = r.RemixRecipe.CookTimeMinutes,
                    Difficulty = r.RemixRecipe.Difficulty.ToDisplayString(),

                    Ingredients = r.RemixRecipe.Ingredients.Select(i => new IngredientDto
                    {
                        IngredientId = i.Id,
                        GlobalIngredientId = i.GlobalIngredientId,
                        Name = i.GlobalIngredient != null ? i.GlobalIngredient.Name : (i.Name ?? ""),
                        Quantity = i.Quantity ?? ""
                    }).ToList(),

                    ParentRecipeId = r.ParentRecipeId,
                    OriginalRecipeId = r.OriginalRecipeId
                })
                .ToListAsync();

            return Ok(remixes);
        }

        [HttpGet("{id:guid}/alerts")]
        [Authorize]
        public async Task<ActionResult<RecipeAlertsResponse>> GetAlerts(Guid id, [FromServices] IAllergyAlertService alerts, CancellationToken ct)
        {
            if (!User.TryGetUserId(out var userId))
                return Unauthorized();

            var result = await alerts.GetAlertsForRecipeAsync(id, userId, ct);
            return Ok(result);
        }

        /// <summary>
        /// Record a view for a recipe (used for trending algorithm)
        /// Rate limited: 1 view per IP per recipe per hour
        /// </summary>
        [HttpPost("{id:guid}/view")]
        [AllowAnonymous]
        public async Task<IActionResult> RecordView(Guid id, CancellationToken ct)
        {
            // Verify recipe exists
            var recipeExists = await _context.Recipes.AnyAsync(r => r.Id == id, ct);
            if (!recipeExists)
                return NotFound();

            // Get IP hash for deduplication (privacy-preserving)
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var ipHash = ComputeIpHash(ipAddress);

            // Check for duplicate view within the last hour
            var oneHourAgo = DateTime.UtcNow.AddHours(-1);
            var alreadyViewed = await _context.RecipeViews
                .AnyAsync(v => v.RecipeId == id && v.IpHash == ipHash && v.ViewedAt > oneHourAgo, ct);

            if (alreadyViewed)
                return NoContent(); // Silently accept but don't record duplicate

            // Record the view
            var userId = CurrentUserId();
            var view = new RecipeView
            {
                RecipeId = id,
                UserId = userId,
                IpHash = ipHash,
                UserAgent = Request.Headers.UserAgent.ToString().Length > 512
                    ? Request.Headers.UserAgent.ToString()[..512]
                    : Request.Headers.UserAgent.ToString(),
                Referrer = Request.Headers.Referer.ToString().Length > 1024
                    ? Request.Headers.Referer.ToString()[..1024]
                    : Request.Headers.Referer.ToString(),
                ViewedAt = DateTime.UtcNow
            };

            _context.RecipeViews.Add(view);
            await _context.SaveChangesAsync(ct);

            return NoContent();
        }

        private static string ComputeIpHash(string ipAddress)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(ipAddress + "_platerly_salt_2026");
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
