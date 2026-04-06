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
using Platerly.Api.Services.Nickel;
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

    /// <summary>
    /// Core B2C controller for the Recipzee recipe graph.
    /// Handles full recipe lifecycle (create, read, update, soft-delete), image uploads,
    /// allergen highlighting with severity mapping, tag management, comments/replies/likes,
    /// reposts, and premium-gated remixing.
    /// Base route: <c>api/recipe</c>.
    /// Read endpoints are anonymous; all write and allergen endpoints require a valid JWT.
    /// Allergen resolution is performed server-side using <see cref="IAllergenResolver"/>
    /// and respects the <c>?allergyMin=low|medium|high</c> query parameter.
    /// Ingredient violations are validated against the prohibited list on every create/update.
    /// </summary>
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
        private readonly INickelLoadService _nickelLoad;

        public RecipeController(
            PlaterlyDbContext context,
            IBlobService blobService,
            ILogger<RecipeController> log,
            IAllergenResolver allergenResolver,
            IAllergyAlertService alerts,
            INotificationService notificationService,
            IIngredientValidationService ingredientValidation,
            IIngredientUsageService ingredientUsage,
            INickelLoadService nickelLoad)
        {
            _context = context;
            _blobService = blobService;
            _log = log;
            _allergenResolver = allergenResolver;
            _alerts = alerts;
            _notificationService = notificationService;
            _ingredientValidation = ingredientValidation;
            _ingredientUsage = ingredientUsage;
            _nickelLoad = nickelLoad;
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

        // ---------------------- Create ----------------------

        /// <summary>
        /// Create a new recipe owned by the authenticated user.
        /// Ingredients are validated against the prohibited ingredient list before saving.
        /// Each ingredient name is resolved to (or creates) a <c>GlobalIngredient</c> row
        /// to maintain the canonical ingredient catalogue. Tags are upserted race-safely.
        /// A URL-friendly slug is generated from the title. Ingredient usage is logged
        /// asynchronously (fire-and-forget) for B2B analytics.
        /// </summary>
        /// <param name="dto">Recipe fields: title, description, instructions, region, ingredients,
        /// tags, prepTimeMinutes, cookTimeMinutes, difficulty, type, imageUrl, imageBlobName.</param>
        /// <returns>
        /// <c>200 OK</c> — created recipe object including id, slug, urlPath, tagNames,
        /// ingredients (with IDs).<br/>
        /// <c>400</c> — ingredient violation (prohibited list)
        /// (<c>{ message, violations: [{ ingredient, category, reason }] }</c>).<br/>
        /// <c>401</c> — unauthenticated.
        /// </returns>
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
                // SEO-friendly URL path: /username/slug (GitHub-style)
                UrlPath = $"/{username}/{recipe.Slug}",
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

        /// <summary>
        /// Upload a recipe image to blob storage before the recipe is created or updated.
        /// Returns the URL and blob name to embed in the subsequent create/update request.
        /// Max file size: 5 MB.
        /// </summary>
        /// <param name="file">The image file to upload.</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ imageUrl: string, imageBlobName: string }</c>.<br/>
        /// <c>400</c> — no file, or file too large.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — user not found.
        /// </returns>
        // Upload one-off image (frontend helper)
        [Authorize]
        [HttpPost("upload-recipe-image")]
        public async Task<IActionResult> UploadRecipeImage(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5MB — matches frontend validation
            if (file.Length > MaxFileSizeBytes)
                return BadRequest("Image exceeds the maximum allowed size of 5MB.");

            if (!User.TryGetUserId(out var userId))
                return Unauthorized();

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound("User not found.");

            var fileName = $"{user.Username}_recipe_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var imageUrl = await _blobService.UploadRecipeImageAsync(file, fileName);

            return Ok(new { imageUrl, imageBlobName = fileName });
        }

        // ---------------------- Read ----------------------

        /// <summary>
        /// Retrieve a recipe by its GUID, including full allergen highlighting for the viewer.
        /// If the request is authenticated, the viewer's allergy profile is loaded and used to
        /// compute per-ingredient severity scores (<c>high</c>, <c>medium</c>, <c>low</c>).
        /// An optional <c>?allergies=nickel,peanut</c> override can be used by dev/test clients.
        /// The <c>?allergyMin=low|medium|high</c> parameter controls the minimum severity threshold
        /// (defaults to "low"). The response also includes remix provenance metadata if the recipe
        /// is itself a remix.
        /// </summary>
        /// <param name="id">The recipe GUID.</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ recipe: RecipeDto, tags, tagNames, allergy, remixMeta }</c>.
        /// <c>RecipeDto.MatchedAllergenSeverity</c> maps normalized ingredient names to severity strings.
        /// <c>RecipeDto.Ingredients</c> includes per-row <c>IsAllergen</c> and <c>AllergySeverity</c>.<br/>
        /// <c>404</c> — recipe not found.
        /// </returns>
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
                UrlPath = !string.IsNullOrEmpty(recipe.Slug) ? $"/{authorUsername}/{recipe.Slug}" : null,
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
                    var hits = AllergenTextUtils.ComputeHitsFromKeywords(expandedForHits, ingredientNames);
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
        /// Retrieve a recipe using its SEO-friendly slug and author username
        /// (e.g., <c>/api/recipe/by-slug/johndoe/chocolate-chip-cookies</c>).
        /// Delegates to <see cref="GetRecipeById"/> after resolving the recipe GUID,
        /// so the full allergen highlighting and remix metadata apply here too.
        /// </summary>
        /// <param name="username">The recipe author's username (case-insensitive).</param>
        /// <param name="slug">The recipe's URL slug (case-insensitive).</param>
        /// <returns>
        /// Same response shape as <see cref="GetRecipeById"/>.<br/>
        /// <c>400</c> — missing username or slug.<br/>
        /// <c>404</c> — user or recipe not found.
        /// </returns>
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
        /// Query recipes by one or more tag names. When no tags are provided, returns all
        /// non-deleted recipes ordered by creation date descending.
        /// </summary>
        /// <param name="tags">Comma-separated tag names (e.g., <c>vegan,halal</c>).</param>
        /// <param name="mode">
        /// <c>any</c> (default) — match recipes that have at least one of the tags;
        /// <c>all</c> — match only recipes that have every tag.
        /// </param>
        /// <param name="page">1-based page number (default 1).</param>
        /// <param name="pageSize">Results per page, clamped 1–50 (default 20).</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ total, items, page, pageSize }</c>. Each item includes
        /// id, title, description, imageUrl, createdAt, username, tags (csv), tagNames.
        /// </returns>
        // GET /api/recipe?tags=vegan,halal&mode=any|all&page=1&pageSize=20
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

        /// <summary>
        /// Update a recipe using multipart/form-data, supporting an optional replacement image.
        /// Only the owning user may update. Soft-deleted recipes return <c>410 Gone</c>.
        /// Tags are replaced wholesale (not merged). If a new image is provided, the old blob
        /// is deleted best-effort after the DB is updated.
        /// </summary>
        /// <param name="id">The recipe GUID to update.</param>
        /// <param name="dto">Partial recipe update; null fields are ignored. Optional image file.</param>
        /// <returns>
        /// <c>200 OK</c> — updated recipe snapshot (id, title, description, instructions, region,
        /// tags, tagNames, prepTime, cookTime, difficulty, imageUrl, updatedAt, ingredients).<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>403</c> — caller is not the recipe owner.<br/>
        /// <c>404</c> — recipe not found.<br/>
        /// <c>409</c> — concurrent update conflict.<br/>
        /// <c>410</c> — recipe has been soft-deleted.
        /// </returns>
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

        /// <summary>
        /// Update a recipe via JSON body. Supports optional full ingredient replacement
        /// (when <c>Ingredients</c> is provided, the existing set is deleted and rebuilt
        /// inside a transaction). Ingredients are validated against the prohibited list.
        /// Only the owning user may update. Wrapped in a DB transaction; rolls back on any error.
        /// </summary>
        /// <param name="id">The recipe GUID to update.</param>
        /// <param name="dto">Text fields and optional ingredient list. Null fields are ignored.</param>
        /// <returns>
        /// <c>200 OK</c> — updated recipe snapshot with fresh ingredient IDs.<br/>
        /// <c>400</c> — ingredient violation.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>403</c> — caller is not the recipe owner.<br/>
        /// <c>404</c> — recipe not found.<br/>
        /// <c>409</c> — concurrent update conflict.<br/>
        /// <c>410</c> — recipe has been soft-deleted.
        /// </returns>
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

        /// <summary>
        /// Soft-delete a recipe owned by the authenticated user. Sets <c>IsDeleted = true</c>,
        /// records <c>DeletedAt</c> and <c>DeletedByUserId</c>. The record is retained in the
        /// database for referential integrity. Idempotent — already-deleted recipes return 204.
        /// </summary>
        /// <param name="id">The recipe GUID to soft-delete.</param>
        /// <returns>
        /// <c>204 No Content</c> on success (including already-deleted).<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>403</c> — caller is not the recipe owner.<br/>
        /// <c>404</c> — recipe not found.
        /// </returns>
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

        /// <summary>
        /// Like a recipe. Fires a like notification to the recipe owner.
        /// Idempotent guard: returns 400 if already liked.
        /// </summary>
        /// <param name="id">The recipe GUID to like.</param>
        /// <returns>
        /// <c>200 OK</c> — "Recipe liked."<br/>
        /// <c>400</c> — already liked.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — recipe not found.
        /// </returns>
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

        /// <summary>
        /// Remove a previously placed like from a recipe.
        /// </summary>
        /// <param name="id">The recipe GUID to unlike.</param>
        /// <returns>
        /// <c>200 OK</c> — "Recipe unliked."<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — like not found.
        /// </returns>
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

        /// <summary>
        /// Post a top-level comment on a recipe. @mention usernames in the content are parsed
        /// and each mentioned user receives an in-app notification. The recipe owner also
        /// receives a comment notification (unless they are the commenter).
        /// </summary>
        /// <param name="recipeId">The recipe GUID to comment on.</param>
        /// <param name="dto">Comment content (plain text; @mentions are parsed automatically).</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="CommentDto"/> with author info.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — recipe not found.
        /// </returns>
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

        /// <summary>
        /// Retrieve all comments for a recipe in a nested tree structure. Top-level comments
        /// are ordered newest-first; replies within each thread are ordered oldest-first.
        /// If the request is authenticated, <c>IsLiked</c> reflects the viewer's like state
        /// per comment.
        /// </summary>
        /// <param name="recipeId">The recipe GUID.</param>
        /// <returns>
        /// <c>200 OK</c> — array of <see cref="CommentDto"/> with nested <c>Replies</c>.
        /// </returns>
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

        /// <summary>
        /// Edit the content of a comment. Only the comment author may edit.
        /// </summary>
        /// <param name="recipeId">The parent recipe GUID.</param>
        /// <param name="commentId">The comment GUID to update.</param>
        /// <param name="dto">New comment content.</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ id, content, updatedAt }</c>.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>403</c> — caller is not the comment author.<br/>
        /// <c>404</c> — comment not found.
        /// </returns>
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

        /// <summary>
        /// Delete a comment and all its replies. Either the comment author or the recipe owner
        /// may delete.
        /// </summary>
        /// <param name="recipeId">The parent recipe GUID.</param>
        /// <param name="commentId">The comment GUID to delete.</param>
        /// <returns>
        /// <c>204 No Content</c> on success.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>403</c> — caller is neither the comment author nor the recipe owner.<br/>
        /// <c>404</c> — comment not found.
        /// </returns>
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

        /// <summary>
        /// Like a comment. Returns 400 if already liked.
        /// </summary>
        /// <param name="recipeId">The parent recipe GUID.</param>
        /// <param name="commentId">The comment GUID to like.</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ liked: true, likesCount: int }</c>.<br/>
        /// <c>400</c> — already liked.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — comment not found.
        /// </returns>
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

        /// <summary>
        /// Remove a previously placed like from a comment.
        /// </summary>
        /// <param name="recipeId">The parent recipe GUID.</param>
        /// <param name="commentId">The comment GUID to unlike.</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ liked: false, likesCount: int }</c>.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — like not found.
        /// </returns>
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

        /// <summary>
        /// Post a reply to a top-level comment. Replies to replies are not permitted
        /// (max one level of nesting). The parent comment owner receives a reply notification.
        /// @mentions in the reply content are parsed and notified.
        /// </summary>
        /// <param name="recipeId">The parent recipe GUID.</param>
        /// <param name="commentId">The parent (top-level) comment GUID to reply to.</param>
        /// <param name="dto">Reply content.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="CommentDto"/> for the new reply.<br/>
        /// <c>400</c> — attempting to reply to a reply.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — parent comment not found.
        /// </returns>
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

        /// <summary>
        /// Repost a recipe to the authenticated user's activity feed. Notifies the recipe owner.
        /// Returns 400 if the user has already reposted.
        /// </summary>
        /// <param name="id">The recipe GUID to repost.</param>
        /// <returns>
        /// <c>200 OK</c> — "Recipe reposted successfully."<br/>
        /// <c>400</c> — already reposted.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — recipe not found.
        /// </returns>
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

        /// <summary>
        /// Remove a repost from the authenticated user's activity feed.
        /// </summary>
        /// <param name="id">The recipe GUID to un-repost.</param>
        /// <returns>
        /// <c>200 OK</c> — "Repost removed successfully."<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — repost not found.
        /// </returns>
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

        /// <summary>
        /// Create a remixed version of an existing recipe. Requires Premium or an active trial;
        /// free users receive a <c>403</c> with code <c>remix_requires_premium</c>.
        /// The remix is saved as a new recipe with a <c>RecipeRemix</c> provenance record linking
        /// it back to the original (and the ultimate original in a chain). Ingredients are
        /// validated against the prohibited list. Tags are upserted.
        /// </summary>
        /// <param name="originalId">The GUID of the recipe being remixed.</param>
        /// <param name="dto">The remixed recipe payload (same shape as create).</param>
        /// <returns>
        /// <c>200 OK</c> — created remix recipe object including <c>remixOf</c> and
        /// <c>originalRecipeId</c> fields.<br/>
        /// <c>400</c> — ingredient violation or invalid payload.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>403</c> — user is not on Premium or active trial
        /// (<c>{ message, code: "remix_requires_premium" }</c>).<br/>
        /// <c>404</c> — original recipe not found.
        /// </returns>
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

            // Premium gate — remix requires premium or active trial
            var remixUser = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.IsPremium, u.TrialEndUtc })
                .FirstOrDefaultAsync();

            var trialActive = remixUser?.TrialEndUtc.HasValue == true
                && remixUser.TrialEndUtc.Value > DateTimeOffset.UtcNow;

            if (remixUser == null || (!remixUser.IsPremium && !trialActive))
            {
                return StatusCode(403, new
                {
                    message = "Recipe remixing requires Premium",
                    code = "remix_requires_premium"
                });
            }

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
        public async Task<ActionResult<RecipeAlertsResponse>> GetAlerts(
            Guid id,
            [FromServices] IAllergyAlertService alerts,
            [FromServices] Platerly.Api.Services.Allergy.IAllergenScanService scanService,
            CancellationToken ct)
        {
            if (!User.TryGetUserId(out var userId))
                return Unauthorized();

            var allowed = await scanService.TryConsumeOrCountScanAsync(userId, id, ct);
            if (!allowed)
            {
                var status = await scanService.GetWeeklyScanStatusAsync(userId, ct);
                return StatusCode(402, new
                {
                    error = "scan_limit_reached",
                    scansUsed = status.ScansUsed,
                    scansLimit = status.ScansLimit,
                    weekResetsAt = status.WeekResetsAt
                });
            }

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

        /// <summary>
        /// Calculate nickel load for a user-created recipe.
        /// Builds a single-meal NickelDailyLoadRequest from the recipe's ingredients
        /// and delegates to NickelLoadService. Only relevant to users who have flagged
        /// nickel sensitivity — the frontend gates this call behind that check.
        /// </summary>
        [Authorize]
        [HttpGet("{id:guid}/nickel-load")]
        public async Task<IActionResult> GetNickelLoad(Guid id, CancellationToken ct)
        {
            var recipe = await _context.Recipes
                .Include(r => r.Ingredients).ThenInclude(i => i.GlobalIngredient)
                .FirstOrDefaultAsync(r => r.Id == id, ct);

            if (recipe == null)
                return NotFound();

            var ingredientInputs = recipe.Ingredients
                .Select(i => new NickelIngredientInput
                {
                    Name = (i.GlobalIngredient != null ? i.GlobalIngredient.Name : i.Name) ?? string.Empty,
                })
                .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                .ToList();

            if (ingredientInputs.Count == 0)
                return Ok(new NickelDailyLoadResponse());

            var request = new NickelDailyLoadRequest
            {
                Meals = new List<NickelMealInput>
                {
                    new NickelMealInput
                    {
                        Label = recipe.Title ?? "Recipe",
                        Ingredients = ingredientInputs,
                    }
                }
            };

            var result = await _nickelLoad.CalculateDailyLoadAsync(request, ct);
            return Ok(result);
        }
    }
}
