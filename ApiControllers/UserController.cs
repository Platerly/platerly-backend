using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.Data;
using Platerly.Api.Models;
using Platerly.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
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
