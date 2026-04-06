using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.Data;
using Platerly.Api.Models;
using Platerly.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Text.Json;
using Platerly.Api.Services;
using Stripe;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2C controller for user profile management, image uploads, social graph, and
    /// PIPEDA compliance operations (data export / account deletion).
    /// Base route: <c>api/user</c>.
    /// Public read endpoints are anonymous; all write endpoints require a valid JWT.
    /// </summary>
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

        /// <summary>
        /// Replace the authenticated user's profile image (preferred route).
        /// Uploads the new blob first, persists the new URL and blob name to the database,
        /// then performs a best-effort deletion of the previous blob.
        /// </summary>
        /// <param name="input">Multipart form containing the image file (max 8 MB, must be an image MIME type).</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ imageUrl: string }</c> with the new SAS/CDN URL.<br/>
        /// <c>400</c> — file missing, too large, or wrong MIME type.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>500</c> — blob upload failure.
        /// </returns>
        // PUT /api/user/update-profile-image
        [Authorize]
        [HttpPut("update-profile-image")]
        [Consumes("multipart/form-data")]
        public Task<IActionResult> UpdateProfileImage([FromForm] FileUploadDto input)
            => UpdateProfileImageCore(input.File);

        /// <summary>
        /// Backward-compatible alias for <see cref="UpdateProfileImage"/>. Delegates to the same
        /// upload logic. Kept to avoid breaking existing mobile/web clients.
        /// </summary>
        /// <param name="input">Multipart form containing the image file (max 8 MB).</param>
        /// <returns>Same response shape as <see cref="UpdateProfileImage"/>.</returns>
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

        /// <summary>
        /// Retrieve a user's public profile by their GUID.
        /// Only safe, non-sensitive fields are returned (no email, billing, or allergy data).
        /// </summary>
        /// <param name="id">The user's unique identifier.</param>
        /// <returns>
        /// <c>200 OK</c> — public profile object (id, username, displayName, bio, profileImageUrl,
        /// bannerImageUrl, createdAt, social links).<br/>
        /// <c>404</c> — user not found.
        /// </returns>
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

        /// <summary>
        /// Retrieve the full profile of the authenticated user, including premium/billing fields
        /// and allergy preferences. This is the primary endpoint used by the frontend to hydrate
        /// the current session.
        /// </summary>
        /// <returns>
        /// <c>200 OK</c> — <see cref="UserDto"/> with all profile, billing, and role fields.<br/>
        /// <c>401</c> — missing or invalid JWT claim.<br/>
        /// <c>404</c> — user record not found.
        /// </returns>
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

        /// <summary>
        /// Update editable profile fields for the authenticated user.
        /// Profile images must be changed via the dedicated upload endpoints, not this route.
        /// Bio is silently capped at 500 characters. Social link URLs are normalized to include
        /// an <c>https://</c> scheme if absent.
        /// </summary>
        /// <param name="dto">Fields to update; null values are ignored (patch semantics).</param>
        /// <returns>
        /// <c>204 No Content</c> on success.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — user not found.
        /// </returns>
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

        /// <summary>
        /// Retrieve a user's public profile page, including their most recent recipes.
        /// Username lookup is case-insensitive.
        /// </summary>
        /// <param name="username">The target user's username.</param>
        /// <returns>
        /// <c>200 OK</c> — object with <c>user</c> (public fields + social links + region)
        /// and <c>recipes</c> (array of summaries ordered by creation date desc).<br/>
        /// <c>404</c> — username not found.
        /// </returns>
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

        /// <summary>
        /// Upload or replace the authenticated user's profile banner image.
        /// Follows the same safe-swap pattern as profile images: upload first,
        /// save new blob metadata to the database, then best-effort delete the old blob.
        /// </summary>
        /// <param name="input">Multipart form with the banner image file (max 8 MB, image MIME).</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ imageUrl: string, blobName: string }</c>.<br/>
        /// <c>400</c> — validation error.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>500</c> — blob upload failure.
        /// </returns>
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

        /// <summary>
        /// List all users who follow the specified username.
        /// </summary>
        /// <param name="username">The target user's username (case-insensitive).</param>
        /// <returns>
        /// <c>200 OK</c> — array of <see cref="FollowUserDto"/> (id, username, displayName, profileImageUrl).<br/>
        /// <c>404</c> — username not found.
        /// </returns>
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

        /// <summary>
        /// List all users that the specified username follows.
        /// </summary>
        /// <param name="username">The target user's username (case-insensitive).</param>
        /// <returns>
        /// <c>200 OK</c> — array of <see cref="FollowUserDto"/>.<br/>
        /// <c>404</c> — username not found.
        /// </returns>
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

        /// <summary>
        /// Follow another user. Idempotent — if already following, returns 204 without error.
        /// </summary>
        /// <param name="id">The GUID of the user to follow.</param>
        /// <returns>
        /// <c>204 No Content</c> on success (including already-following).<br/>
        /// <c>401</c> — unauthenticated.
        /// </returns>
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

        /// <summary>
        /// Unfollow a user the authenticated user is currently following.
        /// </summary>
        /// <param name="id">The GUID of the user to unfollow.</param>
        /// <returns>
        /// <c>204 No Content</c> on success.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — follow relationship not found.
        /// </returns>
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

        /// <summary>
        /// Quick user search by username or display name (for @-mention autocomplete and people search).
        /// Returns at most 25 results regardless of the requested <paramref name="limit"/>.
        /// </summary>
        /// <param name="q">Search term (max 50 characters).</param>
        /// <param name="limit">Maximum results to return (clamped 1–25, default 10).</param>
        /// <returns>
        /// <c>200 OK</c> — array of <c>{ id, username, displayName }</c>.<br/>
        /// <c>400</c> — query too long.
        /// </returns>
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

        /// <summary>
        /// List all public cookbooks owned by the given username, ordered by creation date descending.
        /// </summary>
        /// <param name="username">The owner's username (case-insensitive).</param>
        /// <returns>
        /// <c>200 OK</c> — array of <c>{ id, title, isPublic, createdAt }</c>.<br/>
        /// <c>404</c> — username not found.
        /// </returns>
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

        /// <summary>
        /// Return the 100 most recently registered users for the Explore / Discover page.
        /// Returns only safe public fields (no email or billing data).
        /// </summary>
        /// <returns><c>200 OK</c> — array of <c>{ username, displayName, bio, profileImageUrl }</c>.</returns>
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
        /// Check whether a candidate username is available and meets format requirements.
        /// Used during the onboarding flow before committing a username change.
        /// Rules: 3–30 characters, alphanumeric + underscore only.
        /// </summary>
        /// <param name="username">The desired username to validate.</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ available: bool, message?: string }</c>.<br/>
        /// <c>400</c> — username query string is empty.
        /// </returns>
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
        /// Generate a set of random, available username suggestions using adjective+noun word pools
        /// with optional numeric suffixes (Reddit-style). Suggestions are verified against the
        /// database before being returned, so every result is guaranteed to be available at the
        /// time of the call.
        /// </summary>
        /// <param name="count">Number of suggestions to return (clamped 1–10, default 5).</param>
        /// <returns><c>200 OK</c> — <c>{ suggestions: string[] }</c>.</returns>
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
        /// Export all personal data held for the authenticated user as a downloadable JSON file
        /// (PIPEDA / right-of-access compliance). Includes profile, recipes, comments, allergies,
        /// follow relationships, shopping lists, and meal plans.
        /// The exported file is named <c>recipzee-data-{username}-{date}.json</c>.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — <c>application/json</c> file attachment.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — user not found.
        /// </returns>
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

            var cookbooks = await _context.Cookbooks
                .AsNoTracking()
                .Where(c => c.UserId == userId.Value)
                .Select(c => new { c.Id, c.Title, c.IsPublic, c.CreatedAt })
                .ToListAsync(ct);

            var allergenScans = await _context.UserAllergenScans
                .AsNoTracking()
                .Where(s => s.UserId == userId.Value)
                .Select(s => new { s.RecipeId, s.WeekStartDate, s.ScannedAt })
                .ToListAsync(ct);

            var recipeLikes = await _context.RecipeLikes
                .AsNoTracking()
                .Where(l => l.UserId == userId.Value)
                .Select(l => new { l.RecipeId, l.LikedAt })
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
                    user.WebsiteUrl,
                    user.YoutubeUrl,
                    user.XUrl,
                    user.InstagramUrl,
                    user.AllergyKeywords
                },
                subscription = new
                {
                    user.IsPremium,
                    user.PremiumStatus,
                    user.PremiumSince,
                    user.PremiumCancelAtPeriodEnd,
                    user.PremiumCurrentPeriodEnd,
                    user.TrialStartUtc,
                    user.TrialEndUtc
                },
                recipes,
                comments,
                allergies,
                following,
                followers,
                cookbooks,
                allergenScans,
                recipeLikes,
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
        /// Permanently delete the authenticated user's account and cascade-remove all associated
        /// data (PIPEDA / right-to-erasure compliance). This action is irreversible.
        /// User-authored recipes are soft-deleted to preserve referential integrity; all other
        /// data (tokens, notifications, allergies, follow records, likes, comments, shopping lists,
        /// meal plans, cookbooks) is hard-deleted. Profile and banner blobs are deleted best-effort.
        /// Requires the request body: <c>{ "confirmation": "delete my account" }</c>.
        /// </summary>
        /// <param name="dto">Confirmation payload — must contain the exact phrase "delete my account".</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>204 No Content</c> on success.<br/>
        /// <c>400</c> — confirmation phrase missing or incorrect.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — user not found.
        /// </returns>
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

            // Cancel Stripe subscription immediately before deletion
            if (!string.IsNullOrWhiteSpace(user.StripeSubscriptionId))
            {
                try
                {
                    var stripeKey = _config["Stripe:SecretKey"];
                    if (!string.IsNullOrWhiteSpace(stripeKey))
                    {
                        StripeConfiguration.ApiKey = stripeKey;
                        var subService = new SubscriptionService();
                        await subService.CancelAsync(user.StripeSubscriptionId, cancellationToken: ct);
                        _log.LogInformation("Stripe subscription {SubId} cancelled for account deletion (user {UserId})", user.StripeSubscriptionId, userId);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to cancel Stripe subscription {SubId} during account deletion for user {UserId} — proceeding with deletion", user.StripeSubscriptionId, userId);
                }
            }

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
        /// Update the authenticated user's username and/or display name. Intended for the
        /// post-registration onboarding flow. Username changes are validated for uniqueness and
        /// format (3–30 chars, alphanumeric + underscore). Null fields are ignored.
        /// </summary>
        /// <param name="dto">New username and/or display name.</param>
        /// <returns>
        /// <c>200 OK</c> — updated user summary (id, username, displayName, email,
        /// profileImageUrl, bio, isEmailVerified, createdAt).<br/>
        /// <c>400</c> — validation failure or username already taken.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — user not found.
        /// </returns>
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
