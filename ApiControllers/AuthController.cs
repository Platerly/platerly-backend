using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Platerly.Api.Data;
using Platerly.Api.Helpers;
using Platerly.Api.Models;
using Platerly.Api.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Platerly.Api.ApiControllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly PlaterlyDbContext _db;
        private readonly JwtTokenGenerator _jwt;
        private readonly IConfiguration _config;
        private readonly AuthCookieService _cookies;

        public AuthController(PlaterlyDbContext db, JwtTokenGenerator jwt, IConfiguration config, AuthCookieService cookies)
        {
            _db = db; _jwt = jwt; _config = config; _cookies = cookies;
        }

        /* ───────────────── DTOs ───────────────── */
        public class LoginRequest { public string Email { get; set; } = ""; public string Password { get; set; } = ""; }
        public class LoginResponse { public string Token { get; set; } = ""; public object User { get; set; } = default!; }

        /* ──────────────── Helpers ─────────────── */
        private int RefreshDays()
        {
            // More defensive than int.Parse(...!): default to 30 if missing/malformed
            var s = _config["Jwt:RefreshTokenDays"];
            if (!int.TryParse(s, out var days) || days <= 0) days = 30;
            return days;
        }

        private bool IsActive(RefreshToken rt)
            => rt.RevokedAtUtc == null && rt.ExpiresAtUtc > DateTime.UtcNow;

        private RefreshToken BuildRefreshToken(Guid userId)
        {
            var newPlain = TokenHashing.NewRandomToken();
            var newHash = TokenHashing.Hash(newPlain);

            return new RefreshToken
            {
                UserId = userId,
                TokenHash = newHash,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddDays(RefreshDays()),
                UserAgent = Request.Headers.UserAgent.ToString(),
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                // We'll return the plain string separately to set the cookie
            };
        }

        private void SetRefreshCookieFromPlain(string plain, DateTime expiresUtc)
        {
            // Use cookie service (host-only domain recommended in service for now)
            _cookies.SetRefreshCookie(Response, plain, expiresUtc);

            // DEBUG header so we can see from curl/PowerShell that we attempted to set it
            Response.Headers["X-Refresh-Cookie-Attempt"] = $"ok; exp={expiresUtc:o}";
        }

        /// <summary>
        /// When we detect a refresh token reuse attack (old token presented after rotation),
        /// revoke the entire chain (current token + all descendants).
        /// </summary>
        private async Task RevokeTokenFamily(RefreshToken root)
        {
            // Revoke the presented/related token and any downstream tokens in the replacement chain
            var family = await _db.RefreshTokens
                .Where(r => r.UserId == root.UserId)
                .ToListAsync();

            foreach (var rt in family.Where(r => r.RevokedAtUtc == null))
            {
                rt.RevokedAtUtc = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
        }

        /* ───────────────── Endpoints ──────────────── */

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email);
            if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
                return Unauthorized(new { message = "Invalid credentials." });

            var accessToken = _jwt.GenerateToken(user);

            // Create & store refresh token (hash in DB, plain in cookie)
            var plain = TokenHashing.NewRandomToken();
            var hash = TokenHashing.Hash(plain);

            var refresh = new RefreshToken
            {
                UserId = user.Id,
                TokenHash = hash,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddDays(RefreshDays()),
                UserAgent = Request.Headers.UserAgent.ToString(),
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            };

            _db.RefreshTokens.Add(refresh);
            await _db.SaveChangesAsync();

            SetRefreshCookieFromPlain(plain, refresh.ExpiresAtUtc);

            var payload = new LoginResponse
            {
                Token = accessToken,
                User = new
                {
                    id = user.Id,
                    email = user.Email,
                    username = user.Username,
                    displayName = user.DisplayName,
                    profileImageUrl = user.ProfileImageUrl,
                    isEmailVerified = user.IsEmailVerified
                }
            };

            return Ok(payload);
        }

        [HttpPost("refresh")]
        [AllowAnonymous]
        public async Task<IActionResult> Refresh()
        {
            var cookiePlain = _cookies.ReadRefreshCookie(Request);
            if (string.IsNullOrEmpty(cookiePlain))
                return Unauthorized(new { message = "Missing refresh token." });

            var incomingHash = TokenHashing.Hash(cookiePlain);

            var token = await _db.RefreshTokens
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.TokenHash == incomingHash);

            // Case: Not found. Could be an outright invalid token. Deny.
            if (token == null)
                return Unauthorized(new { message = "Invalid refresh token." });

            // If token is revoked, it's very likely a reuse attempt.
            if (token.RevokedAtUtc != null)
            {
                await RevokeTokenFamily(token);
                _cookies.ClearRefreshCookie(Response);
                return Unauthorized(new { message = "Refresh token reuse detected. All sessions revoked." });
            }

            // If expired, deny and clear cookie.
            if (token.ExpiresAtUtc <= DateTime.UtcNow)
            {
                token.RevokedAtUtc = DateTime.UtcNow; // hard revoke expired to avoid reuse windows
                _db.RefreshTokens.Update(token);
                await _db.SaveChangesAsync();
                _cookies.ClearRefreshCookie(Response);
                return Unauthorized(new { message = "Refresh token expired." });
            }

            // Happy path: rotate (single-use)
            using var tx = await _db.Database.BeginTransactionAsync();

            try
            {
                // Revoke the old
                token.RevokedAtUtc = DateTime.UtcNow;

                // Create the replacement
                var newPlain = TokenHashing.NewRandomToken();
                var newHash = TokenHashing.Hash(newPlain);

                var replacement = new RefreshToken
                {
                    UserId = token.UserId,
                    TokenHash = newHash,
                    CreatedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.AddDays(RefreshDays()),
                    UserAgent = Request.Headers.UserAgent.ToString(),
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
                };

                token.ReplacedByTokenHash = newHash;

                _db.RefreshTokens.Update(token);
                _db.RefreshTokens.Add(replacement);
                await _db.SaveChangesAsync();

                await tx.CommitAsync();

                // Set the fresh cookie
                SetRefreshCookieFromPlain(newPlain, replacement.ExpiresAtUtc);

                // Issue a fresh access token
                var newAccess = _jwt.GenerateToken(token.User);
                return Ok(new { token = newAccess });
            }
            catch
            {
                await tx.RollbackAsync();
                return StatusCode(500, new { message = "Failed to refresh token." });
            }
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var cookie = _cookies.ReadRefreshCookie(Request);
            if (!string.IsNullOrEmpty(cookie))
            {
                var hash = TokenHashing.Hash(cookie);
                var rt = await _db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash && r.RevokedAtUtc == null);
                if (rt != null)
                {
                    rt.RevokedAtUtc = DateTime.UtcNow;
                    _db.RefreshTokens.Update(rt);
                    await _db.SaveChangesAsync();
                }
            }

            _cookies.ClearRefreshCookie(Response);
            return NoContent();
        }

        /// <summary>
        /// Optional: revoke all sessions for the current user (server-side sign out everywhere).
        /// Requires auth (bearer) for the access token.
        /// </summary>
        [HttpPost("logout-all")]
        [Authorize]
        public async Task<IActionResult> LogoutAll()
        {
            // Identify the current user from the access token
            var userIdClaim = User?.FindFirst("sub")?.Value ?? User?.FindFirst("id")?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var tokens = await _db.RefreshTokens.Where(r => r.UserId == userId && r.RevokedAtUtc == null).ToListAsync();
            foreach (var t in tokens) t.RevokedAtUtc = DateTime.UtcNow;
            _db.RefreshTokens.UpdateRange(tokens);
            await _db.SaveChangesAsync();

            _cookies.ClearRefreshCookie(Response);
            return NoContent();
        }

        /* ───────────────── DEBUG ONLY (remove after verifying cookies) ───────────────── */

        // 1) Prove app can set ANY cookie at all
        [HttpGet("debug-cookie")]
        [AllowAnonymous]
        public IActionResult DebugCookie()
        {
            Response.Cookies.Append(
                "platerly_debug",
                "1",
                new CookieOptions { Path = "/", IsEssential = true }
            );

            Response.Headers["X-Set-Debug-Cookie"] = "attempted";
            return Ok(new { ok = true });
        }

        // 2) Try setting the refresh cookie inline (bypasses AuthCookieService)
        //    to isolate whether the service/options are the issue.
        [HttpPost("debug-refresh-cookie")]
        [AllowAnonymous]
        public IActionResult DebugRefreshCookieInline()
        {
            var plain = TokenHashing.NewRandomToken();
            var expiresUtc = DateTime.UtcNow.AddDays(RefreshDays());

            var opts = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                IsEssential = true,
                Expires = expiresUtc,
                Path = "/"
                // Domain intentionally omitted (host-only) while we debug
            };

            try
            {
                Response.Cookies.Append("platerly.r", plain, opts);
                Response.Headers["X-Debug-RefreshCookie-Attempt"] = $"ok; exp={expiresUtc:o}";
                return Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                Response.Headers["X-Debug-RefreshCookie-Error"] = ex.GetType().Name + ":" + ex.Message;
                return StatusCode(500, new { ok = false, error = ex.Message });
            }
        }
    }
}
