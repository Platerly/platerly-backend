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
            // Use cookie service
            _cookies.SetRefreshCookie(Response, plain, expiresUtc);

            // DEBUG header so we can see from curl/PowerShell that we attempted to set it
            Response.Headers["X-Refresh-Cookie-Attempt"] = $"ok; exp={expiresUtc:o}";
        }

        /// <summary>
        /// When we detect a refresh token reuse attack (old token presented after rotation),
        /// revoke the entire chain (current token + all descendants).
        /// (Currently unused in sliding-token mode, kept for future hardening.)
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

            // 🔴 NEW: set access-token cookie so JwtBearer can authenticate via cookie
            var accessMinutesStr = _config["Jwt:AccessTokenMinutes"];
            int accessMinutes = 15;
            if (int.TryParse(accessMinutesStr, out var parsed) && parsed > 0)
                accessMinutes = parsed;

            _cookies.SetAccessCookie(Response, accessToken, DateTime.UtcNow.AddMinutes(accessMinutes));

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

            // Case: Not found. Could be an outright invalid token. Deny & clear cookie.
            if (token == null)
            {
                _cookies.ClearRefreshCookie(Response);
                return Unauthorized(new { message = "Invalid refresh token." });
            }

            // If token is revoked (logout / logout-all), deny and clear cookie.
            if (token.RevokedAtUtc != null)
            {
                _cookies.ClearRefreshCookie(Response);
                return Unauthorized(new { message = "Refresh token has been revoked." });
            }

            // If expired, deny, mark revoked, and clear cookie.
            if (token.ExpiresAtUtc <= DateTime.UtcNow)
            {
                token.RevokedAtUtc = DateTime.UtcNow; // mark as dead
                _db.RefreshTokens.Update(token);
                await _db.SaveChangesAsync();

                _cookies.ClearRefreshCookie(Response);
                return Unauthorized(new { message = "Refresh token expired." });
            }

            // ✅ Happy path: sliding token
            // Extend expiry, keep same token, issue new access token.
            token.ExpiresAtUtc = DateTime.UtcNow.AddDays(RefreshDays());
            token.UserAgent = Request.Headers.UserAgent.ToString();
            token.IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            _db.RefreshTokens.Update(token);
            await _db.SaveChangesAsync();

            // Re-write the same refresh token cookie with extended expiry
            SetRefreshCookieFromPlain(cookiePlain, token.ExpiresAtUtc);

            // Issue a fresh access token
            var newAccess = _jwt.GenerateToken(token.User);

            // NEW: update access-token cookie too
            var accessMinutesStr = _config["Jwt:AccessTokenMinutes"];
            int accessMinutes = 15;
            if (int.TryParse(accessMinutesStr, out var parsed) && parsed > 0)
                accessMinutes = parsed;

            _cookies.SetAccessCookie(Response, newAccess, DateTime.UtcNow.AddMinutes(accessMinutes));
            return Ok(new { token = newAccess });
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var cookie = _cookies.ReadRefreshCookie(Request);
            if (!string.IsNullOrEmpty(cookie))
            {
                var hash = TokenHashing.Hash(cookie);
                var rt = await _db.RefreshTokens
                    .FirstOrDefaultAsync(r => r.TokenHash == hash && r.RevokedAtUtc == null);

                if (rt != null)
                {
                    rt.RevokedAtUtc = DateTime.UtcNow;
                    _db.RefreshTokens.Update(rt);
                    await _db.SaveChangesAsync();
                }
            }

            // Clear via helper (host-only current cookies)
            _cookies.ClearRefreshCookie(Response);
            _cookies.ClearAccessCookie(Response);

            // Extra safety: explicitly delete any legacy cookies with various domains
            var hostOnlyOpts = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/"
            };

            var dotPlaterlyOpts = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/",
                Domain = ".platerly.com"
            };

            var dotWwwPlaterlyOpts = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/",
                Domain = ".www.platerly.com"
            };

            // Host-only (e.g. "www.platerly.com")
            Response.Cookies.Delete("platerly.r", hostOnlyOpts);
            Response.Cookies.Delete("platerly.a", hostOnlyOpts);

            // Domain-scoped ".platerly.com"
            Response.Cookies.Delete("platerly.r", dotPlaterlyOpts);
            Response.Cookies.Delete("platerly.a", dotPlaterlyOpts);

            // Domain-scoped ".www.platerly.com"
            Response.Cookies.Delete("platerly.r", dotWwwPlaterlyOpts);
            Response.Cookies.Delete("platerly.a", dotWwwPlaterlyOpts);

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

            var tokens = await _db.RefreshTokens
                .Where(r => r.UserId == userId && r.RevokedAtUtc == null)
                .ToListAsync();

            foreach (var t in tokens)
                t.RevokedAtUtc = DateTime.UtcNow;

            _db.RefreshTokens.UpdateRange(tokens);
            await _db.SaveChangesAsync();

            // Clear via helper
            _cookies.ClearRefreshCookie(Response);
            _cookies.ClearAccessCookie(Response);

            // Extra safety: delete all known domain variants
            var hostOnlyOpts = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/"
            };

            var dotPlaterlyOpts = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/",
                Domain = ".platerly.com"
            };

            var dotWwwPlaterlyOpts = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/",
                Domain = ".www.platerly.com"
            };

            Response.Cookies.Delete("platerly.r", hostOnlyOpts);
            Response.Cookies.Delete("platerly.a", hostOnlyOpts);

            Response.Cookies.Delete("platerly.r", dotPlaterlyOpts);
            Response.Cookies.Delete("platerly.a", dotPlaterlyOpts);

            Response.Cookies.Delete("platerly.r", dotWwwPlaterlyOpts);
            Response.Cookies.Delete("platerly.a", dotWwwPlaterlyOpts);

            return NoContent();
        }



    }
}
