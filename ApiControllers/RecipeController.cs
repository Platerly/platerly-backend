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
        public RecipeController(
            PlaterlyDbContext context,
            IBlobService blobService,
            ILogger<RecipeController> log,
            IAllergenResolver allergenResolver,
            IAllergyAlertService alerts)
        {
            _context = context;
            _blobService = blobService;
            _log = log;
            _allergenResolver = allergenResolver;
            _alerts = alerts;
        }

        private Guid? CurrentUserId()
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(id, out var g) ? g : (Guid?)null;
        }

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
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

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
                UserId = Guid.Parse(userId),
                CreatedAt = DateTime.UtcNow,
                PrepTimeMinutes = dto.PrepTimeMinutes,
                CookTimeMinutes = dto.CookTimeMinutes,
                Difficulty = DifficultyExtensions.ParseDifficultyOrNull(dto.Difficulty),
                Ingredients = normalizedIngredients,
                RecipeTags = new List<RecipeTag>()
            };

            var incomingTagNames = NormalizeNames(dto.Tags ?? new List<string>());
            var tagLookup = await EnsureTagsExistAsync(incomingTagNames);
            ReplaceTagsWithNames(recipe, incomingTagNames, tagLookup);

            _context.Recipes.Add(recipe);
            await _context.SaveChangesAsync();

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
                recipe.CreatedAt,
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

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _context.Users.FindAsync(Guid.Parse(userId!));
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

            var recipeDto = new RecipeDto
            {
                Id = recipe.Id,
                Title = recipe.Title,
                Description = recipe.Description,
                Instructions = recipe.Instructions,
                Region = recipe.Region,
                ImageUrl = recipe.ImageUrl,
                CreatedAt = recipe.CreatedAt,
                AuthorUsername = recipe.User?.Username ?? "Unknown",

                // we'll fill Ingredients AFTER allergen calc so we can attach flags per row
                Ingredients = new List<IngredientDto>(),

                // ✅ List<string> (matches feed)
                Tags = tagNames.ToList(),

                IngredientNames = ingredientNames,

                PrepTimeMinutes = recipe.PrepTimeMinutes,
                CookTimeMinutes = recipe.CookTimeMinutes,
                Difficulty = recipe.Difficulty.ToDisplayString(),

                MatchedAllergenTerms = new List<string>(),
                MatchedAllergenSeverity = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            };

            // ---------------- helpers ----------------
            static string Norm(string s) =>
                Regex.Replace((s ?? "").Trim().ToLowerInvariant(), @"\s+", " ");

            // -------- legacy highlight payload (kept for compatibility) --------
            object allergyPayload = new
            {
                enabled = false,
                keywords = Array.Empty<string>(),
                hits = Array.Empty<object>()
            };

            // =========================================================================
            // NEW (Option A): DB-driven allergy alerts (canonical + future-proof)
            // Replaces the entire old "ExpandKeywords / string contains" pipeline.
            // =========================================================================
            try
            {
                if (AllergyRequestEnabled(HttpContext.Request) && User.Identity?.IsAuthenticated == true)
                {
                    var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (Guid.TryParse(uid, out var viewerId))
                    {
                        // NOTE: this assumes RecipeController has an injected IAllergyAlertService _alerts
                        // (see notes below)
                        var resp = await _alerts.GetAlertsForRecipeAsync(recipe.Id, viewerId);

                        // Build severity per ingredient (normalized ingredient name -> "high|medium|low")
                        var severityMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        static int Rank(string? s) => s?.ToLowerInvariant() switch
                        {
                            "high" => 3,
                            "medium" => 2,
                            "low" => 1,
                            _ => 1
                        };

                        static string PickHigher(string a, string b) => Rank(a) >= Rank(b) ? a : b;

                        if (resp?.Alerts != null)
                        {
                            foreach (var alert in resp.Alerts)
                            {
                                if (alert?.Ingredients == null) continue;

                                foreach (var ing in alert.Ingredients)
                                {
                                    var key = Norm(ing.IngredientName ?? "");
                                    if (string.IsNullOrWhiteSpace(key)) continue;

                                    var sev = (ing.SeverityResolved ?? "Low").ToLowerInvariant();

                                    if (!severityMap.TryGetValue(key, out var cur))
                                        severityMap[key] = sev;
                                    else
                                        severityMap[key] = PickHigher(cur, sev);
                                }
                            }
                        }

                        recipeDto.MatchedAllergenSeverity = severityMap;
                        recipeDto.MatchedAllergenTerms = severityMap.Keys.ToList();

                        // legacy payload: map alert buckets -> ingredient hits
                        allergyPayload = new
                        {
                            enabled = severityMap.Count > 0,
                            // allergy buckets the user is hitting (canonical names like "Milk", "Gluten", "Nickel")
                            keywords = resp?.Alerts?
                                .Select(a => a.AllergenType)
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList() ?? new List<string>(),

                            hits = (resp?.Alerts?
    .SelectMany(a => (a.Ingredients ?? new List<IngredientAlertDetailDto>())
        .Select(x => (object)new
        {
            ingredientIndex = ingredientNames.FindIndex(n =>
                string.Equals(Norm(n), Norm(x.IngredientName), StringComparison.OrdinalIgnoreCase)),
            ingredientName = x.IngredientName,
            matchedKeyword = a.AllergenType
        }))
    .Where(o => ((dynamic)o).ingredientIndex >= 0) // keep only valid indexes
    .ToList())
    ?? new List<object>()

                        };
                    }
                }
            }
            catch
            {
                recipeDto.MatchedAllergenTerms = new List<string>();
                recipeDto.MatchedAllergenSeverity = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            // -------- attach per-row ingredient flags & IDs --------
            static string Key(string s) =>
                Regex.Replace((s ?? "").Trim().ToLowerInvariant(), @"\s+", " ");

            var ingredientsWithFlags = recipe.Ingredients.Select(i =>
            {
                var resolvedName = i.GlobalIngredient != null ? i.GlobalIngredient.Name : (i.Name ?? "");
                var key = Key(resolvedName);
                recipeDto.MatchedAllergenSeverity.TryGetValue(key, out var sev);

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
                // legacy CSV + modern array
                tags = string.Join(",", tagNames),
                tagNames,
                // highlight payload for older clients
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
                    Tags = string.Join(",", ToTagNameArray(r)),
                    tagNames = ToTagNameArray(r)
                })
                .ToListAsync();

            return Ok(new { total, items, page, pageSize });
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

                if (dto.Tags != null)
                {
                    var names = NormalizeNames(dto.Tags);
                    var lookup = await EnsureTagsExistAsync(names);
                    ReplaceTagsWithNames(recipe, names, lookup);
                }

                if (dto.Ingredients != null)
                {
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
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var recipe = await _context.Recipes.FindAsync(id);
            if (recipe == null) return NotFound("Recipe not found.");

            var alreadyLiked = await _context.RecipeLikes
                .AnyAsync(rl => rl.RecipeId == id && rl.UserId == Guid.Parse(userId));

            if (alreadyLiked)
                return BadRequest("You already liked this recipe.");

            var like = new RecipeLike
            {
                RecipeId = id,
                UserId = Guid.Parse(userId),
                LikedAt = DateTime.UtcNow
            };

            _context.RecipeLikes.Add(like);
            await _context.SaveChangesAsync();

            return Ok("Recipe liked.");
        }

        [Authorize]
        [HttpDelete("{id}/like")]
        public async Task<IActionResult> UnlikeRecipe(Guid id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var like = await _context.RecipeLikes
                .FirstOrDefaultAsync(rl => rl.RecipeId == id && rl.UserId == Guid.Parse(userId));

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
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var recipe = await _context.Recipes.FindAsync(recipeId);
            if (recipe == null) return NotFound("Recipe not found.");

            var comment = new Comment
            {
                Content = dto.Content,
                CreatedAt = DateTime.UtcNow,
                RecipeId = recipeId,
                UserId = Guid.Parse(userId)
            };

            _context.Comments.Add(comment);
            await _context.SaveChangesAsync();

            return Ok(new { comment.Id, comment.Content, comment.CreatedAt });
        }

        [AllowAnonymous]
        [HttpGet("{recipeId}/comments")]
        public async Task<ActionResult<IEnumerable<CommentDto>>> GetComments(Guid recipeId)
        {
            var comments = await _context.Comments
                .Where(c => c.RecipeId == recipeId)
                .Include(c => c.User)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new CommentDto
                {
                    Id = c.Id,
                    Content = c.Content,
                    CreatedAt = c.CreatedAt,
                    UserId = c.UserId,
                    Username = c.User!.Username,
                    DisplayName = c.User.DisplayName,
                    ProfileImageUrl = c.User.ProfileImageUrl
                })
                .ToListAsync();

            return Ok(comments);
        }

        [Authorize]
        [HttpPost("{id}/repost")]
        public async Task<IActionResult> RepostRecipe(Guid id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var existingRecipe = await _context.Recipes.FindAsync(id);
            if (existingRecipe == null)
                return NotFound("Recipe not found.");

            var userGuid = Guid.Parse(userId);

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

            return Ok("Recipe reposted successfully.");
        }

        [Authorize]
        [HttpDelete("{id}/repost")]
        public async Task<IActionResult> UnrepostRecipe(Guid id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var userGuid = Guid.Parse(userId);

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

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

            var originalRecipe = await _context.Recipes.FirstOrDefaultAsync(r => r.Id == originalId);
            if (originalRecipe == null) return NotFound("Original recipe not found.");

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

            return Ok(new { remixId = remix.Id });
        }

        [AllowAnonymous]
        [HttpGet("{id}/lineage")]
        public async Task<IActionResult> GetRemixLineage(Guid id)
        {
            var lineage = new List<RecipeDto>();
            var currentRecipeId = id;

            while (true)
            {
                var recipe = await _context.Recipes
                    .Include(r => r.Ingredients)
                    .Include(r => r.User)
                    .Include(r => r.RecipeTags).ThenInclude(rt => rt.Tag)
                    .FirstOrDefaultAsync(r => r.Id == currentRecipeId);

                if (recipe == null) break;

                lineage.Insert(0, new RecipeDto
                {
                    Id = recipe.Id,
                    Title = recipe.Title,
                    Description = recipe.Description,
                    Instructions = recipe.Instructions,
                    Region = recipe.Region,
                    ImageUrl = recipe.ImageUrl,
                    CreatedAt = recipe.CreatedAt,
                    Ingredients = recipe.Ingredients.Select(i => new IngredientDto
                    {
                        IngredientId = i.Id,
                        GlobalIngredientId = i.GlobalIngredientId,
                        Name = i.GlobalIngredient != null ? i.GlobalIngredient.Name : (i.Name ?? ""),
                        Quantity = i.Quantity ?? ""
                    }).ToList(),
                    Tags = ToTagNameArray(recipe).ToList(),
                    PrepTimeMinutes = recipe.PrepTimeMinutes,
                    CookTimeMinutes = recipe.CookTimeMinutes,
                    Difficulty = recipe.Difficulty.ToDisplayString(),
                });

                var remixRecord = await _context.RecipeRemixes
                    .FirstOrDefaultAsync(r => r.RemixRecipeId == currentRecipeId);

                if (remixRecord == null || remixRecord.OriginalRecipeId == null)
                    break;

                currentRecipeId = remixRecord.OriginalRecipeId.Value;
            }

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
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var result = await alerts.GetAlertsForRecipeAsync(id, userId, ct);
            return Ok(result);
        }
    }
}
