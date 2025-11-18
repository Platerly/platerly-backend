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
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdStr, out var userId))
                return Unauthorized();

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return NotFound("User not found.");

            var oldBlobName = user.ProfileImageBlobName;
            var newBlobName = $"{user.Username}_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";

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

        [HttpGet("{id}")]
        public async Task<ActionResult<User>> GetUser(Guid id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            return Ok(user);
        }

        [Authorize]
        [HttpGet("me")]
        public async Task<ActionResult<UserDto>> GetCurrentUser()
        {
            // 1) Safely get user id from claims
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            {
                return Unauthorized(new
                {
                    error = "unauthorized",
                    reason = "invalid_or_missing_user_id_claim"
                });
            }

            // 2) Load user
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return NotFound(new
                {
                    error = "not_found",
                    reason = "user_not_found"
                });
            }

            // 3) Build banner URL from blob name
            string? bannerImageUrl = null;
            if (!string.IsNullOrWhiteSpace(user.BannerImageBlobName))
            {
                var storageBase = _config["AzureStorage:PublicBaseUrl"];
                var bannerContainer = _config["AzureStorage:BannerContainerName"] ?? "profile-banners-dev";

                if (!string.IsNullOrWhiteSpace(storageBase))
                {
                    bannerImageUrl = $"{storageBase.TrimEnd('/')}/{bannerContainer}/{user.BannerImageBlobName}";
                }
            }

            // 4) Map to DTO, including premium + billing fields
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
                BannerImageUrl = bannerImageUrl,

                IsPremium = user.IsPremium,
                PremiumStatus = user.PremiumStatus,
                PremiumSince = user.PremiumSince,
                PremiumCancelAtPeriodEnd = user.PremiumCancelAtPeriodEnd,
                PremiumCurrentPeriodEnd = user.PremiumCurrentPeriodEnd,
                BillingEmail = user.BillingEmail
            };

            return Ok(dto);
        }

        [Authorize]
        [HttpPut("me")]
        public async Task<IActionResult> UpdateCurrentUser([FromBody] UpdateUserDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var user = await _context.Users.FindAsync(Guid.Parse(userId));
            if (user == null) return NotFound();

            // Basic fields
            user.DisplayName = dto.DisplayName ?? user.DisplayName;

            if (dto.Bio != null)
            {
                user.Bio = dto.Bio.Length > 500 ? dto.Bio[..500] : dto.Bio; // soft cap at 500
            }

            user.ProfileImageUrl = dto.ProfileImageUrl ?? user.ProfileImageUrl;

            // Social / website links (normalize http(s) scheme)
            if (dto.WebsiteUrl != null) user.WebsiteUrl = NormalizeUrl(dto.WebsiteUrl);
            if (dto.YouTubeUrl != null) user.YoutubeUrl = NormalizeUrl(dto.YouTubeUrl);
            if (dto.XUrl != null) user.XUrl = NormalizeUrl(dto.XUrl);
            if (dto.InstagramUrl != null) user.InstagramUrl = NormalizeUrl(dto.InstagramUrl);

            // Allergies
            if (dto.AllergyKeywords != null)
                user.AllergyKeywords = dto.AllergyKeywords;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        [AllowAnonymous]
        [HttpGet("profile/{username}")]
        public async Task<IActionResult> GetUserProfile(string username)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

            if (user == null) return NotFound("User not found.");

            // Build banner URL from blob name (dev uses profile-banners-dev)
            string? bannerImageUrl = null;
            if (!string.IsNullOrWhiteSpace(user.BannerImageBlobName))
            {
                var storageBase = _config["AzureStorage:PublicBaseUrl"];
                var bannerContainer = _config["AzureStorage:BannerContainerName"] ?? "profile-banners-dev";
                if (!string.IsNullOrWhiteSpace(storageBase))
                    bannerImageUrl = $"{storageBase.TrimEnd('/')}/{bannerContainer}/{user.BannerImageBlobName}";
            }

            var recipes = await _context.Recipes
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
                    InstagramUrl = user.InstagramUrl
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
            if (file is null || file.Length == 0) return BadRequest("No file uploaded.");
            if (!file.ContentType.StartsWith("image/")) return BadRequest("Invalid file type.");
            if (file.Length > 8_000_000) return BadRequest("Max 8MB.");

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null) return NotFound("User not found.");

            var newBlobName = $"{user.Username}_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";

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

        [AllowAnonymous]
        [HttpGet("{username}/followers")]
        public async Task<ActionResult<IEnumerable<FollowUserDto>>> GetFollowersOfUser(string username)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
            if (user == null) return NotFound("User not found.");

            var followers = await _context.UserFollows
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
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
            if (user == null) return NotFound("User not found.");

            var following = await _context.UserFollows
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
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId == null) return Unauthorized();

            var alreadyFollowing = await _context.UserFollows.AnyAsync(f =>
                f.FollowerId == Guid.Parse(currentUserId) && f.FollowingId == id);

            if (alreadyFollowing) return NoContent();

            _context.UserFollows.Add(new UserFollow
            {
                FollowerId = Guid.Parse(currentUserId),
                FollowingId = id
            });

            await _context.SaveChangesAsync();
            return NoContent();
        }

        [Authorize]
        [HttpDelete("{id}/follow")]
        public async Task<IActionResult> UnfollowUser(Guid id)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId == null) return Unauthorized();

            var follow = await _context.UserFollows.FirstOrDefaultAsync(f =>
                f.FollowerId == Guid.Parse(currentUserId) && f.FollowingId == id);

            if (follow == null) return NotFound();

            _context.UserFollows.Remove(follow);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // GET /api/User/search?q=alice
        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] int limit = 10)
        {
            if (string.IsNullOrWhiteSpace(q)) return Ok(Array.Empty<object>());

            q = q.Trim();
            var users = await _context.Users
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
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
            if (user == null) return NotFound("User not found.");

            var cookbooks = await _context.Cookbooks
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
    }
}
