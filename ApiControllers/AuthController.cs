using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Platerly.Api.Data;
using Platerly.Api.Helpers;
using Platerly.Api.Models;
using Platerly.Api.Services;
using System;
using System.IdentityModel.Tokens.Jwt;

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

        public class LoginRequest { public string Email { get; set; } = ""; public string Password { get; set; } = ""; }
        public class LoginResponse { public string Token { get; set; } = ""; public object User { get; set; } = default!; }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email);
            if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
                return Unauthorized(new { message = "Invalid credentials." });

            var accessToken = _jwt.GenerateToken(user);

            // Create refresh token
            var rtDays = int.Parse(_config["Jwt:RefreshTokenDays"]!);
            var refreshPlain = TokenHashing.NewRandomToken();
            var refreshHash = TokenHashing.Hash(refreshPlain);
            var refresh = new RefreshToken
            {
                UserId = user.Id,
                TokenHash = refreshHash,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddDays(rtDays),
                UserAgent = Request.Headers.UserAgent.ToString(),
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            };
            _db.RefreshTokens.Add(refresh);
            await _db.SaveChangesAsync();

            // Set cookie (HttpOnly)
            _cookies.SetRefreshCookie(Response, refreshPlain, refresh.ExpiresAtUtc);

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
            var cookie = _cookies.ReadRefreshCookie(Request);
            if (string.IsNullOrEmpty(cookie)) return Unauthorized();

            var hash = TokenHashing.Hash(cookie);
            var existing = await _db.RefreshTokens
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.TokenHash == hash);

            if (existing == null || !existing.IsActive)
                return Unauthorized();

            // Rotate: revoke the old, create a new
            existing.RevokedAtUtc = DateTime.UtcNow;

            var rtDays = int.Parse(_config["Jwt:RefreshTokenDays"]!);
            var newPlain = TokenHashing.NewRandomToken();
            var newHash = TokenHashing.Hash(newPlain);

            var replacement = new RefreshToken
            {
                UserId = existing.UserId,
                TokenHash = newHash,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddDays(rtDays),
                UserAgent = Request.Headers.UserAgent.ToString(),
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            };
            existing.ReplacedByTokenHash = newHash;

            _db.RefreshTokens.Update(existing);
            _db.RefreshTokens.Add(replacement);
            await _db.SaveChangesAsync();

            _cookies.SetRefreshCookie(Response, newPlain, replacement.ExpiresAtUtc);

            // Issue new access token
            var accessToken = _jwt.GenerateToken(existing.User);
            return Ok(new { token = accessToken });
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
    }
}
