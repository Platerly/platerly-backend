using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.Data;
using Platerly.Api.Models;
using Platerly.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Text.Json;
using Platerly.Api.Services;

namespace Platerly.Api.ApiControllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly PlaterlyDbContext _context;
        private readonly IBlobService _blobService;
        private readonly IConfiguration _config;
        private readonly ILogger<UserController> _log;

        private const long MaxImageBytes = 8_000_000; // 8MB

        public UserController(
            PlaterlyDbContext context,
            IBlobService blobService,
            IConfiguration config,
            ILogger<UserController> log)
        {
            _context = context;
            _blobService = blobService;
            _config = config;
            _log = log;
        }

        // Normalizes user-provided URLs (adds https:// if missing, trims whitespace)
        private static string? NormalizeUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            url = url.Trim();
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }
            return url;
        }

        private Guid? GetUserIdFromClaims()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(userIdStr, out var id) ? id : null;
        }

        // Banner URLs are now stored in the database as SAS URLs (like profile images)
        // This method is kept for backward compatibility during migration
        private string? GetBannerUrl(User user)
        {
            // Prefer stored SAS URL
            if (!string.IsNullOrWhiteSpace(user.BannerImageUrl))
                return user.BannerImageUrl;

            // Fallback: no URL stored (pre-migration data)
            return null;
        }

        private static bool IsImageUploadValid(IFormFile? file, out string error)
        {
            error = "";

            if (file is null || file.Length == 0)
            {
                error = "No file uploaded.";
                return false;
            }

            if (file.Length > MaxImageBytes)
            {
                error = "Max 8MB.";
                return false;
            }

            // Most reliable quick check here
            if (string.IsNullOrWhiteSpace(file.ContentType) || !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                error = "Invalid file type.";
                return false;
            }

            return true;
        }

        private static string SafeImageExtension(IFormFile file)
        {
            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(ext) || ext.Length > 10) return ".jpg";
            if (!ext.StartsWith(".")) ext = "." + ext;

            // Optional: restrict common image extensions
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".webp", ".gif"
            };

            return allowed.Contains(ext) ? ext : ".jpg";
        }

        // ============================================================
        // Profile Image: Safe Swap (upload new -> save DB -> delete old)
        // ============================================================

        // PUT /api/user/update-profile-image
        [Authorize]
        [HttpPut("update-profile-image")]
        [Consumes("multipart/form-data")]
        public Task<IActionResult> UpdateProfileImage([FromForm] FileUploadDto input)
            => UpdateProfileImageCore(input.File);

        // POST /api/user/upload-profile-image (back-compat)
        [Authorize]
        [HttpPost("upload-profile-image")]
        [Consumes("multipart/form-data")]
        public Task<IActionResult> UploadProfileImage([FromForm] FileUploadDto input)
            => UpdateProfileImageCore(input.File);

        // Shared logic for both routes (Swagger-safe)
        private async Task<IActionResult> UpdateProfileImageCore(IFormFile file)
        {
            if (!IsImageUploadValid(file, out var error))
                return BadRequest(error);

            var userId = GetUserIdFromClaims();
            if (userId is null) return Unauthorized();

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId.Value);
            if (user == null) return NotFound("User not found.");

            var oldBlobName = user.ProfileImageBlobName;

            var ext = SafeImageExtension(file);
            var newBlobName = $"{user.Username}_{Guid.NewGuid()}{ext}";

            string newUrl;
            try
            {
                newUrl = await _blobService.UploadProfileImageAsync(file, newBlobName);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Upload failed for user {UserId}", userId);
                return StatusCode(StatusCodes.Status500InternalServerError, "Upload failed.");
            }

            user.ProfileImageUrl = newUrl;
            user.ProfileImageBlobName = newBlobName;

            await _context.SaveChangesAsync();

            // best-effort delete of old blob
            try
            {
                if (!string.IsNullOrWhiteSpace(oldBlobName) && oldBlobName != newBlobName)
                    await _blobService.DeleteProfileImageIfExistsAsync(oldBlobName);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to delete old profile image blob {Blob}", oldBlobName);
            }

            return Ok(new { imageUrl = newUrl });
        }

        // ============================================================
        // ✅ SAFE: Get User by Id (DO NOT RETURN ENTITY)
        // ============================================================

        // GET /api/user/{id}
        [AllowAnonymous]
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetUser(Guid id)
        {
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null) return NotFound();

            // return only safe public fields
            return Ok(new
            {
                user.Id,
                user.Username,
                user.DisplayName,
                user.Bio,
                user.ProfileImageUrl,
                BannerImageUrl = GetBannerUrl(user),
                user.CreatedAt,

                WebsiteUrl = user.WebsiteUrl,
                YouTubeUrl = user.YoutubeUrl,
                XUrl = user.XUrl,
                InstagramUrl = user.InstagramUrl
            });
        }

        // ============================================================
        // Current User
        // ============================================================

        [Authorize]
        [HttpGet("me")]
        public async Task<ActionResult<UserDto>> GetCurrentUser()
        {
            // Safely get user id from claims
            var userId = GetUserIdFromClaims();
            if (userId is null)
            {
                return Unauthorized(new
                {
                    error = "unauthorized",
                    reason = "invalid_or_missing_user_id_claim"
                });
            }

            // Load user
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId.Value);
            if (user == null)
            {
                return NotFound(new
                {
                    error = "not_found",
                    reason = "user_not_found"
                });
            }

            var dto = new UserDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                DisplayName = user.DisplayName,
                Bio = user.Bio,
                ProfileImageUrl = user.ProfileImageUrl,
                CreatedAt = user.CreatedAt,
                IsEmailVerified = user.IsEmailVerified,

                WebsiteUrl = user.WebsiteUrl,
                YouTubeUrl = user.YoutubeUrl,
                XUrl = user.XUrl,
                InstagramUrl = user.InstagramUrl,
                BannerImageUrl = GetBannerUrl(user),

                IsPremium = user.IsPremium,
                PremiumStatus = user.PremiumStatus,
                PremiumSince = user.PremiumSince,
                PremiumCancelAtPeriodEnd = user.PremiumCancelAtPeriodEnd,
                PremiumCurrentPeriodEnd = user.PremiumCurrentPeriodEnd,
                BillingEmail = user.BillingEmail,
                Role = user.Role.ToString()
            };

            return Ok(dto);
        }

        [Authorize]
        [HttpPut("me")]
        public async Task<IActionResult> UpdateCurrentUser([FromBody] UpdateUserDto dto)
        {
            var userId = GetUserIdFromClaims();
            if (userId is null) return Unauthorized();

            var user = await _context.Users.FindAsync(userId.Value);
            if (user == null) return NotFound();

            // Basic fields
            user.DisplayName = dto.DisplayName ?? user.DisplayName;

            if (dto.Bio != null)
            {
                user.Bio = dto.Bio.Length > 500 ? dto.Bio[..500] : dto.Bio; // soft cap at 500
            }

            // ❌ Do NOT allow setting ProfileImageUrl directly (must use upload endpoint)
            // user.ProfileImageUrl = dto.ProfileImageUrl ?? user.ProfileImageUrl;

            // Social / website links (normalize http(s) scheme)
            if (dto.WebsiteUrl != null) user.WebsiteUrl = NormalizeUrl(dto.WebsiteUrl);
            if (dto.YouTubeUrl != null) user.YoutubeUrl = NormalizeUrl(dto.YouTubeUrl);
            if (dto.XUrl != null) user.XUrl = NormalizeUrl(dto.XUrl);
            if (dto.InstagramUrl != null) user.InstagramUrl = NormalizeUrl(dto.InstagramUrl);

            // Region preference
            if (dto.Region != null) user.Region = string.IsNullOrWhiteSpace(dto.Region) ? null : dto.Region.Trim().ToLower();

            // Allergies
            if (dto.AllergyKeywords != null)
                user.AllergyKeywords = dto.AllergyKeywords;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // ============================================================
        // Public Profile
        // ============================================================

        [AllowAnonymous]
        [HttpGet("profile/{username}")]
        public async Task<IActionResult> GetUserProfile(string username)
        {
            username = username.Trim();

            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

            if (user == null) return NotFound("User not found.");

            var bannerImageUrl = GetBannerUrl(user);

            var recipes = await _context.Recipes
                .AsNoTracking()
                .Where(r => r.User.Username.ToLower() == username.ToLower())
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new
                {
                    recipeId = r.Id,
                    r.Title,
                    r.Description,
                    r.ImageUrl,
                    r.Likes,
                    r.CreatedAt,
                    AuthorUsername = r.User.Username,
                    Ingredients = r.Ingredients.Select(i => new { i.Name, i.Quantity }),
                })
                .ToListAsync();

            return Ok(new
            {
                user = new
                {
                    user.Id,
                    user.Username,
                    user.DisplayName,
                    user.Bio,
                    user.ProfileImageUrl,
                    BannerImageUrl = bannerImageUrl,
                    user.CreatedAt,

                    WebsiteUrl = user.WebsiteUrl,
                    YouTubeUrl = user.YoutubeUrl,
                    XUrl = user.XUrl,
                    InstagramUrl = user.InstagramUrl,
                    user.Region
                },
                recipes
            });
        }

        // ======================= Banner Upload =======================

        [Authorize]
        [HttpPost("banner")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadBanner([FromForm] FileUploadDto input)
        {
            var file = input?.File;

            if (!IsImageUploadValid(file, out var error))
                return BadRequest(error);

            var userId = GetUserIdFromClaims();
            if (userId is null) return Unauthorized();

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId.Value);
            if (user is null) return NotFound("User not found.");

            var ext = SafeImageExtension(file);
            var newBlobName = $"{user.Username}_{Guid.NewGuid()}{ext}";

            string newUrl;
            try
            {
                newUrl = await _blobService.UploadBannerImageAsync(file, newBlobName);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Banner upload failed for user {UserId}", userId);
                return StatusCode(StatusCodes.Status500InternalServerError, "Upload failed.");
            }

            var oldBlobName = user.BannerImageBlobName;
            user.BannerImageBlobName = newBlobName;
            user.BannerImageUrl = newUrl; // Store SAS URL for private container access
            await _context.SaveChangesAsync();

            // best-effort cleanup
            try
            {
                if (!string.IsNullOrWhiteSpace(oldBlobName) && oldBlobName != newBlobName)
                    await _blobService.DeleteBannerImageIfExistsAsync(oldBlobName);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to delete old banner blob {Blob}", oldBlobName);
            }

            return Ok(new { imageUrl = newUrl, blobName = newBlobName });
        }

        // ============================================================
        // Followers / Following
        // ============================================================

        [AllowAnonymous]
        [HttpGet("{username}/followers")]
        public async Task<ActionResult<IEnumerable<FollowUserDto>>> GetFollowersOfUser(string username)
        {
            username = username.Trim();

            var user = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

            if (user == null) return NotFound("User not found.");

            var followers = await _context.UserFollows
                .AsNoTracking()
                .Where(f => f.FollowingId == user.Id)
                .Include(f => f.Follower)
                .Select(f => new FollowUserDto
                {
                    Id = f.Follower!.Id,
                    Username = f.Follower.Username,
                    DisplayName = f.Follower.DisplayName,
                    ProfileImageUrl = f.Follower.ProfileImageUrl
                })
                .ToListAsync();

            return Ok(followers);
        }

        [AllowAnonymous]
        [HttpGet("{username}/following")]
        public async Task<ActionResult<IEnumerable<FollowUserDto>>> GetFollowingOfUser(string username)
        {
            username = username.Trim();

            var user = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

            if (user == null) return NotFound("User not found.");

            var following = await _context.UserFollows
                .AsNoTracking()
                .Where(f => f.FollowerId == user.Id)
                .Include(f => f.Following)
                .Select(f => new FollowUserDto
                {
                    Id = f.Following!.Id,
                    Username = f.Following.Username,
                    DisplayName = f.Following.DisplayName,
                    ProfileImageUrl = f.Following.ProfileImageUrl
                })
                .ToListAsync();

            return Ok(following);
        }

        [Authorize]
        [HttpPost("{id}/follow")]
        public async Task<IActionResult> FollowUser(Guid id)
        {
            var currentUserId = GetUserIdFromClaims();
            if (currentUserId is null) return Unauthorized();

            var alreadyFollowing = await _context.UserFollows.AnyAsync(f =>
                f.FollowerId == currentUserId.Value && f.FollowingId == id);

            if (alreadyFollowing) return NoContent();

            _context.UserFollows.Add(new UserFollow
            {
                FollowerId = currentUserId.Value,
                FollowingId = id
            });

            await _context.SaveChangesAsync();
            return NoContent();
        }

        [Authorize]
        [HttpDelete("{id}/follow")]
        public async Task<IActionResult> UnfollowUser(Guid id)
        {
            var currentUserId = GetUserIdFromClaims();
            if (currentUserId is null) return Unauthorized();

            var follow = await _context.UserFollows.FirstOrDefaultAsync(f =>
                f.FollowerId == currentUserId.Value && f.FollowingId == id);

            if (follow == null) return NotFound();

            _context.UserFollows.Remove(follow);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // ============================================================
        // Search / Cookbooks / Explore
        // ============================================================

        // GET /api/User/search?q=alice
        [AllowAnonymous]
        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] int limit = 10)
        {
            if (string.IsNullOrWhiteSpace(q)) return Ok(Array.Empty<object>());
            if (q.Length > 50) return BadRequest("Search query too long (max 50 characters)");

            q = q.Trim();

            var users = await _context.Users
                .AsNoTracking()
                .Where(u => u.Username.Contains(q) || (u.DisplayName != null && u.DisplayName.Contains(q)))
                .OrderBy(u => u.Username)
                .Take(Math.Clamp(limit, 1, 25))
                .Select(u => new { id = u.Id, username = u.Username, displayName = u.DisplayName })
                .ToListAsync();

            return Ok(users);
        }

        [AllowAnonymous]
        [HttpGet("profile/{username}/cookbooks")]
        public async Task<IActionResult> GetPublicCookbooksForUser(string username)
        {
            username = username.Trim();

            var user = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

            if (user == null) return NotFound("User not found.");

            var cookbooks = await _context.Cookbooks
                .AsNoTracking()
                .Where(cb => cb.UserId == user.Id && cb.IsPublic)
                .OrderByDescending(cb => cb.CreatedAt)
                .Select(cb => new
                {
                    cb.Id,
                    cb.Title,
                    cb.IsPublic,
                    cb.CreatedAt
                })
                .ToListAsync();

            return Ok(cookbooks);
        }

        [AllowAnonymous]
        [HttpGet("explore")]
        public async Task<IActionResult> ExploreUsers()
        {
            var users = await _context.Users
                .AsNoTracking()
                .OrderByDescending(u => u.CreatedAt)
                .Select(u => new
                {
                    u.Username,
                    u.DisplayName,
                    u.Bio,
                    u.ProfileImageUrl
                })
                .Take(100)
                .ToListAsync();

            return Ok(users);
        }

        // ============================================================
        // Username Check & Profile Update (for onboarding)
        // ============================================================

        /// <summary>
        /// Check if a username is available (for onboarding flow)
        /// </summary>
        [AllowAnonymous]
        [HttpGet("check-username")]
        public async Task<IActionResult> CheckUsername([FromQuery] string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return BadRequest(new { available = false, message = "Username is required" });

            username = username.Trim().ToLower();

            // Validate format
            if (username.Length < 3)
                return Ok(new { available = false, message = "Username must be at least 3 characters" });

            if (username.Length > 30)
                return Ok(new { available = false, message = "Username must be 30 characters or less" });

            if (!System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-zA-Z0-9_]+$"))
                return Ok(new { available = false, message = "Only letters, numbers, and underscores allowed" });

            // Check if taken
            var exists = await _context.Users
                .AsNoTracking()
                .AnyAsync(u => u.Username.ToLower() == username);

            return Ok(new { available = !exists });
        }

        /// <summary>
        /// Generate unique random username suggestions (Reddit-style)
        /// </summary>
        [AllowAnonymous]
        [HttpGet("suggest-usernames")]
        public async Task<IActionResult> SuggestUsernames([FromQuery] int count = 5)
        {
            count = Math.Clamp(count, 1, 10);
            var suggestions = new List<string>();
            var existingUsernames = await _context.Users
                .AsNoTracking()
                .Select(u => u.Username.ToLower())
                .ToListAsync();
            var existingSet = new HashSet<string>(existingUsernames);

            // Word pools for generating fun usernames
            var adjectives = new[] {
                "Happy", "Hungry", "Spicy", "Sweet", "Savory", "Crispy", "Golden", "Cosmic",
                "Mighty", "Clever", "Bold", "Chill", "Fresh", "Zesty", "Tasty", "Epic",
                "Lucky", "Swift", "Bright", "Cool", "Brave", "Noble", "Grand", "Royal"
            };

            var nouns = new[] {
                "Chef", "Cook", "Foodie", "Baker", "Grill", "Kitchen", "Plate", "Fork",
                "Spoon", "Whisk", "Oven", "Pan", "Pot", "Mixer", "Blender", "Spatula",
                "Pepper", "Basil", "Sage", "Cumin", "Thyme", "Ginger", "Garlic", "Onion",
                "Mango", "Berry", "Peach", "Apple", "Lemon", "Lime", "Olive", "Avocado"
            };

            var random = new Random();
            var maxAttempts = count * 20; // Prevent infinite loops
            var attempts = 0;

            while (suggestions.Count < count && attempts < maxAttempts)
            {
                attempts++;
                string candidate;

                // Mix up the patterns
                var pattern = random.Next(4);
                switch (pattern)
                {
                    case 0: // AdjectiveNoun123
                        candidate = $"{adjectives[random.Next(adjectives.Length)]}{nouns[random.Next(nouns.Length)]}{random.Next(10, 999)}";
                        break;
                    case 1: // Adjective_Noun
                        candidate = $"{adjectives[random.Next(adjectives.Length)]}_{nouns[random.Next(nouns.Length)]}";
                        break;
                    case 2: // NounAdjective42
                        candidate = $"{nouns[random.Next(nouns.Length)]}{adjectives[random.Next(adjectives.Length)]}{random.Next(1, 99)}";
                        break;
                    default: // The_Adjective_Noun
                        candidate = $"The{adjectives[random.Next(adjectives.Length)]}{nouns[random.Next(nouns.Length)]}";
                        break;
                }

                var normalized = candidate.ToLower();

                // Ensure unique and not in DB
                if (normalized.Length >= 3 && normalized.Length <= 30 &&
                    !existingSet.Contains(normalized) &&
                    !suggestions.Any(s => s.ToLower() == normalized))
                {
                    suggestions.Add(candidate);
                }
            }

            return Ok(new { suggestions });
        }

        // ============================================================
        // PIPEDA: Data Export
        // ============================================================

        /// <summary>
        /// Download all personal data for the authenticated user (PIPEDA compliance)
        /// </summary>
        [Authorize]
        [HttpGet("data-export")]
        public async Task<IActionResult> DownloadMyData(CancellationToken ct)
        {
            var userId = GetUserIdFromClaims();
            if (userId is null) return Unauthorized();

            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
            if (user == null) return NotFound();

            var recipes = await _context.Recipes
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(r => r.UserId == userId.Value)
                .Select(r => new { r.Id, r.Title, r.Description, r.CreatedAt, r.IsDeleted })
                .ToListAsync(ct);

            var comments = await _context.Comments
                .AsNoTracking()
                .Where(c => c.UserId == userId.Value)
                .Select(c => new { c.Id, c.Content, c.RecipeId, c.CreatedAt })
                .ToListAsync(ct);

            var allergies = await _context.UserAllergies
                .AsNoTracking()
                .Where(a => a.UserId == userId.Value)
                .Include(a => a.Allergy)
                .Select(a => new { a.Allergy.Name })
                .ToListAsync(ct);

            var following = await _context.UserFollows
                .AsNoTracking()
                .Where(f => f.FollowerId == userId.Value)
                .Select(f => new { f.FollowingId })
                .ToListAsync(ct);

            var followers = await _context.UserFollows
                .AsNoTracking()
                .Where(f => f.FollowingId == userId.Value)
                .Select(f => new { f.FollowerId })
                .ToListAsync(ct);

            var shoppingLists = await _context.ShoppingLists
                .AsNoTracking()
                .Where(s => s.UserId == userId.Value)
                .Select(s => new { s.Id, s.Name, s.CreatedAt })
                .ToListAsync(ct);

            var mealPlans = await _context.MealPlans
                .AsNoTracking()
                .Where(m => m.UserId == userId.Value)
                .Select(m => new { m.Id, m.Name, m.StartDate, m.EndDate, m.CreatedAt })
                .ToListAsync(ct);

            var export = new
            {
                exportedAt = DateTime.UtcNow,
                dataSource = "Recipzee / Platerly Inc.",
                profile = new
                {
                    user.Id,
                    user.Username,
                    user.Email,
                    user.DisplayName,
                    user.Bio,
                    user.Region,
                    user.CreatedAt,
                    user.IsEmailVerified,
                    user.IsPremium,
                    user.PremiumStatus,
                    user.WebsiteUrl,
                    user.YoutubeUrl,
                    user.XUrl,
                    user.InstagramUrl,
                    user.AllergyKeywords
                },
                recipes,
                comments,
                allergies,
                following,
                followers,
                shoppingLists,
                mealPlans
            };

            var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            var filename = $"recipzee-data-{user.Username}-{DateTime.UtcNow:yyyyMMdd}.json";
            return File(bytes, "application/json", filename);
        }

        // ============================================================
        // PIPEDA: Delete Account
        // ============================================================

        /// <summary>
        /// Permanently delete the authenticated user's account and all associated data (PIPEDA compliance).
        /// Requires the request body: { "confirmation": "delete my account" }
        /// </summary>
        [Authorize]
        [HttpDelete("account")]
        public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountDto dto, CancellationToken ct)
        {
            if (dto?.Confirmation != "delete my account")
                return BadRequest(new { error = "Type 'delete my account' to confirm." });

            var userId = GetUserIdFromClaims();
            if (userId is null) return Unauthorized();

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
            if (user == null) return NotFound();

            // Remove all user-owned data
            _context.RefreshTokens.RemoveRange(_context.RefreshTokens.Where(t => t.UserId == userId.Value));
            _context.VerificationTokens.RemoveRange(_context.VerificationTokens.Where(t => t.UserId == userId.Value));
            _context.PasswordResetTokens.RemoveRange(_context.PasswordResetTokens.Where(t => t.UserId == userId.Value));
            _context.Notifications.RemoveRange(_context.Notifications.Where(n => n.UserId == userId.Value));
            _context.UserAllergies.RemoveRange(_context.UserAllergies.Where(a => a.UserId == userId.Value));
            _context.UserAllergenScans.RemoveRange(_context.UserAllergenScans.Where(s => s.UserId == userId.Value));
            _context.UserFollows.RemoveRange(_context.UserFollows.Where(f => f.FollowerId == userId.Value || f.FollowingId == userId.Value));
            _context.RecipeLikes.RemoveRange(_context.RecipeLikes.Where(l => l.UserId == userId.Value));
            _context.CommentLikes.RemoveRange(_context.CommentLikes.Where(l => l.UserId == userId.Value));
            _context.Comments.RemoveRange(_context.Comments.Where(c => c.UserId == userId.Value));
            _context.CookbookContributors.RemoveRange(_context.CookbookContributors.Where(c => c.UserId == userId.Value));
            _context.CookbookContributionRequests.RemoveRange(_context.CookbookContributionRequests.Where(c => c.RequestedByUserId == userId.Value));

            // Soft-delete the user's recipes (preserves referential integrity)
            var userRecipes = await _context.Recipes.IgnoreQueryFilters()
                .Where(r => r.UserId == userId.Value).ToListAsync(ct);
            foreach (var r in userRecipes) r.IsDeleted = true;

            // Delete shopping lists and meal plans
            var listIds = await _context.ShoppingLists.Where(s => s.UserId == userId.Value).Select(s => s.Id).ToListAsync(ct);
            _context.ShoppingListItemSources.RemoveRange(_context.ShoppingListItemSources.Where(s => listIds.Contains(s.ShoppingListItemId)));
            _context.ShoppingListItems.RemoveRange(_context.ShoppingListItems.Where(i => listIds.Contains(i.ShoppingListId)));
            _context.ShoppingListRecipes.RemoveRange(_context.ShoppingListRecipes.Where(r => listIds.Contains(r.ShoppingListId)));
            _context.ShoppingLists.RemoveRange(_context.ShoppingLists.Where(s => s.UserId == userId.Value));

            var planIds = await _context.MealPlans.Where(m => m.UserId == userId.Value).Select(m => m.Id).ToListAsync(ct);
            _context.MealPlanEntries.RemoveRange(_context.MealPlanEntries.Where(e => planIds.Contains(e.MealPlanId)));
            _context.MealPlans.RemoveRange(_context.MealPlans.Where(m => m.UserId == userId.Value));

            // Delete cookbooks
            var cookbookIds = await _context.Cookbooks.Where(c => c.UserId == userId.Value).Select(c => c.Id).ToListAsync(ct);
            _context.CookbookRecipes.RemoveRange(_context.CookbookRecipes.Where(cr => cookbookIds.Contains(cr.CookbookId)));
            _context.Cookbooks.RemoveRange(_context.Cookbooks.Where(c => c.UserId == userId.Value));

            // Delete profile images from blob (best effort)
            try
            {
                if (!string.IsNullOrWhiteSpace(user.ProfileImageBlobName))
                    await _blobService.DeleteProfileImageIfExistsAsync(user.ProfileImageBlobName);
                if (!string.IsNullOrWhiteSpace(user.BannerImageBlobName))
                    await _blobService.DeleteBannerImageIfExistsAsync(user.BannerImageBlobName);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to delete blobs for user {UserId}", userId);
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync(ct);

            _log.LogInformation("Account deleted for user {UserId} ({Username})", userId, user.Username);
            return NoContent();
        }

        /// <summary>
        /// Update user profile (username & display name) - for onboarding
        /// </summary>
        [Authorize]
        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
        {
            var userId = GetUserIdFromClaims();
            if (userId is null) return Unauthorized();

            var user = await _context.Users.FindAsync(userId.Value);
            if (user == null) return NotFound();

            // Update username if provided and different
            if (!string.IsNullOrWhiteSpace(dto.Username))
            {
                var newUsername = dto.Username.Trim().ToLower();

                // Validate format
                if (newUsername.Length < 3)
                    return BadRequest(new { message = "Username must be at least 3 characters" });

                if (newUsername.Length > 30)
                    return BadRequest(new { message = "Username must be 30 characters or less" });

                if (!System.Text.RegularExpressions.Regex.IsMatch(newUsername, @"^[a-zA-Z0-9_]+$"))
                    return BadRequest(new { message = "Only letters, numbers, and underscores allowed" });

                // Check if different from current
                if (newUsername != user.Username.ToLower())
                {
                    // Check availability
                    var exists = await _context.Users
                        .AsNoTracking()
                        .AnyAsync(u => u.Username.ToLower() == newUsername && u.Id != userId.Value);

                    if (exists)
                        return BadRequest(new { message = "Username already taken" });

                    user.Username = newUsername;
                }
            }

            // Update display name if provided
            if (dto.DisplayName != null)
            {
                user.DisplayName = dto.DisplayName.Trim();
            }

            await _context.SaveChangesAsync();

            // Return updated user info
            return Ok(new
            {
                user.Id,
                user.Username,
                user.DisplayName,
                user.Email,
                user.ProfileImageUrl,
                user.Bio,
                user.IsEmailVerified,
                user.CreatedAt
            });
        }
    }
}
