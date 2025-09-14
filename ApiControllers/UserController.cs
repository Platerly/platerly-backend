using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.Data;
using Platerly.Api.Models;
using Platerly.Api.DTOs;
using System.Security.Cryptography;
using System.Text;
using Platerly.Api.Helpers;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Platerly.Api.Services;
using BCrypt.Net;

namespace Platerly.Api.ApiControllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly PlaterlyDbContext _context;
        private readonly IBlobService _blobService;
        private readonly JwtTokenGenerator _tokenGen;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _config;
        private readonly ILogger<UserController> _log;

        public UserController(
            PlaterlyDbContext context,
            IBlobService blobService,
            JwtTokenGenerator tokenGen,
            IEmailSender emailSender,
            IConfiguration config,
            ILogger<UserController> log)
        {
            _context = context;
            _blobService = blobService;
            _tokenGen = tokenGen;
            _emailSender = emailSender;
            _config = config;
            _log = log;
        }

        // kept for now; not used since we standardized on BCrypt
        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(password);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterUserDto dto)
        {
            var allowedEmails = new List<string>
            {
                "ayubmja@icloud.com",
                "ayubmja8448@gmail.com",
                "allysonlees@outlook.com",
                "varunraj37@gmail.com",
                "cora.barhett1@gmail.com",
                "brent.maguire@hotmail.com",
                "f0757936@gmail.com",
                "fartunmoxmohamed@gmail.com",
                "winterguy1221@hotmail.com",
                "prabhakaruna1992@gmail.com",
                "maxwelljupp@gmail.com",
                "hassanien.hakeem@gmail.com"
            };

            if (!allowedEmails.Contains(dto.Email.Trim().ToLower()))
                return BadRequest("Registration is currently limited to invited users during the Alpha.");

            if (await _context.Users.AnyAsync(u => u.Email == dto.Email))
                return BadRequest("Email is already registered.");

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

            var user = new User
            {
                Username = dto.Username,
                Email = dto.Email,
                DisplayName = dto.DisplayName,
                Bio = dto.Bio,
                ProfileImageUrl = dto.ProfileImageUrl,
                PasswordHash = passwordHash,
                CreatedAt = DateTime.UtcNow,
                AllergyKeywords = dto.AllergyKeywords ?? new List<string>()
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var token = Guid.NewGuid().ToString();
            var verificationToken = new VerificationToken
            {
                Id = Guid.NewGuid(),
                Token = token,
                UserId = user.Id,
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            };

            _context.VerificationTokens.Add(verificationToken);
            await _context.SaveChangesAsync();

            var frontendBaseUrl = _config["FrontendBaseUrl"] ?? "https://www.platerly.com";
            var verificationLink = $"{frontendBaseUrl}/verify-email?token={token}";

            var subject = "Verify your Platerly account";
            var body = $"Welcome to Platerly! Click here to verify your email: {verificationLink}";

            await _emailSender.SendEmailAsync(user.Email, subject, body);

            var jwtToken = _tokenGen.GenerateToken(user);

            return Ok(new
            {
                token = jwtToken,
                user = new
                {
                    id = user.Id,
                    email = user.Email,
                    username = user.Username,
                    isEmailVerified = user.IsEmailVerified
                }
            });
        }

        [HttpGet("verify-email")]
        public async Task<IActionResult> VerifyEmail([FromQuery] string token)
        {
            var verificationToken = await _context.VerificationTokens
                .FirstOrDefaultAsync(t => t.Token == token && !t.IsUsed);

            if (verificationToken == null)
                return BadRequest("Invalid or already used token.");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == verificationToken.UserId);
            if (user == null)
                return NotFound("User not found.");

            user.IsEmailVerified = true;
            verificationToken.IsUsed = true;

            await _context.SaveChangesAsync();

            return Ok("Email successfully verified.");
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginUserDto dto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null) return Unauthorized("Invalid credentials.");

            var isValid = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
            if (!isValid)
                return Unauthorized("Invalid credentials.");

            var token = _tokenGen.GenerateToken(user);

            return Ok(new
            {
                token,
                user = new
                {
                    id = user.Id,
                    email = user.Email,
                    username = user.Username,
                    isEmailVerified = user.IsEmailVerified,
                }
            });
        }

        [HttpPost("resend-verification-email")]
        public async Task<IActionResult> ResendVerificationEmail([FromBody] ResendVerificationEmailDto dto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null) return NotFound("User not found.");
            if (user.IsEmailVerified) return BadRequest("User is already verified.");

            var token = Guid.NewGuid().ToString();
            var verificationToken = new VerificationToken
            {
                UserId = user.Id,
                Token = token,
                CreatedAt = DateTime.UtcNow,
                IsUsed = false
            };

            _context.VerificationTokens.Add(verificationToken);
            await _context.SaveChangesAsync();

            var verifyUrl = $"{_config["FrontendBaseUrl"]}/verify-email?token={token}";
            var subject = "Resend: Verify your Platerly account";
            var body = $"Please click the following link to verify your email: {verifyUrl}";

            await _emailSender.SendEmailAsync(user.Email, subject, body);

            return Ok("Verification email resent.");
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
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var user = await _context.Users.FindAsync(Guid.Parse(userId));
            if (user == null) return NotFound();

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

            user.DisplayName = dto.DisplayName ?? user.DisplayName;
            user.Bio = dto.Bio ?? user.Bio;
            user.ProfileImageUrl = dto.ProfileImageUrl ?? user.ProfileImageUrl;

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
                .Where(u => u.Username.ToLower() == username.ToLower())
                .Select(u => new
                {
                    u.Id,
                    u.Username,
                    u.DisplayName,
                    u.Bio,
                    u.ProfileImageUrl,
                    u.CreatedAt
                })
                .FirstOrDefaultAsync();

            if (user == null)
                return NotFound("User not found.");

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

            return Ok(new { user, recipes });
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

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto request)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
            if (user == null) return Ok();

            var token = Guid.NewGuid();
            var resetToken = new PasswordResetToken
            {
                UserId = user.Id,
                Token = token,
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                IsUsed = false
            };

            _context.PasswordResetTokens.Add(resetToken);
            await _context.SaveChangesAsync();

            var resetLink = $"{_config["FrontendBaseUrl"]}/reset-password?token={token}";
            await _emailSender.SendPasswordResetEmailAsync(user.Email, "Reset your Platerly password",
                $"Click the link below to reset your password:\n\n{resetLink}");

            return Ok();
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto request)
        {
            var tokenRecord = await _context.PasswordResetTokens
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.Token == request.Token && !t.IsUsed);

            if (tokenRecord == null || tokenRecord.ExpiresAt < DateTime.UtcNow)
                return BadRequest("Invalid or expired token.");

            var user = tokenRecord.User;
            if (user == null) return BadRequest("User not found.");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            tokenRecord.IsUsed = true;

            await _context.SaveChangesAsync();

            return Ok("Password has been reset successfully.");
        }
    }
}
