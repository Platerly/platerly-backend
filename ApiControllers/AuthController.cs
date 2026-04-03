using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Helpers;
using Platerly.Api.Models;
using Platerly.Api.Services;
using Platerly.Api.Services.Meta;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2C authentication controller. Handles all identity operations for Recipzee end-users.
    ///
    /// Base route: /api/Auth
    /// Auth requirement: most endpoints are [AllowAnonymous]; session-mutating endpoints require [Authorize].
    ///
    /// Responsibilities:
    ///   • Local (email/password) registration with 7-day trial activation and email verification.
    ///   • Local login with sliding-window refresh tokens stored as HttpOnly cookies.
    ///   • Google OAuth flow: token verification against Google's /tokeninfo and /userinfo endpoints,
    ///     account lookup/creation, and optional account linking when the email already exists locally.
    ///   • Refresh-token rotation: single-use tokens identified by HMAC-SHA256 hash stored in DB;
    ///     the plain token travels only in the HttpOnly cookie, never in the response body.
    ///   • Logout (current device) and logout-all (revokes entire token family for the user).
    ///   • Password change and set-password (for OAuth-only accounts).
    ///   • Email change flow: stores pending email, sends verification link, confirms on click.
    ///   • Meta Conversions API tracking fired on registration.
    ///
    /// JWT is short-lived (default 15 min); the HttpOnly refresh cookie extends sessions
    /// up to the configured Jwt:RefreshTokenDays (default 30 days, 7 days for non-persistent sessions).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly PlaterlyDbContext _db;
        private readonly JwtTokenGenerator _jwt;
        private readonly IConfiguration _config;
        private readonly AuthCookieService _cookies;
        private readonly IEmailSender _emailSender;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMetaConversionsService _metaConversions;
        // PII note: always log user.Id (Guid), never user.Email or user.Username —
        // emails are PII and must not appear in structured log streams (Sentry, Azure Monitor, etc.).
        private readonly ILogger<AuthController> _log;

        public AuthController(
            PlaterlyDbContext db,
            JwtTokenGenerator jwt,
            IConfiguration config,
            AuthCookieService cookies,
            IEmailSender emailSender,
            IHttpClientFactory httpClientFactory,
            IMetaConversionsService metaConversions,
            ILogger<AuthController> log)
        {
            _db = db;
            _jwt = jwt;
            _config = config;
            _cookies = cookies;
            _emailSender = emailSender;
            _httpClientFactory = httpClientFactory;
            _metaConversions = metaConversions;
            _log = log;
        }

        /* ───────────────── DTOs ───────────────── */
        public class LoginRequest
        {
            [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Email is required")]
            [System.ComponentModel.DataAnnotations.EmailAddress(ErrorMessage = "Invalid email format")]
            [System.ComponentModel.DataAnnotations.MaxLength(256, ErrorMessage = "Email cannot exceed 256 characters")]
            public string Email { get; set; } = "";

            [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Password is required")]
            [System.ComponentModel.DataAnnotations.MaxLength(128, ErrorMessage = "Password cannot exceed 128 characters")]
            public string Password { get; set; } = "";

            public bool RememberMe { get; set; } = false;
        }

        public class LoginResponse
        {
            public string Token { get; set; } = "";
            public object User { get; set; } = default!;
            public bool IsNewUser { get; set; } = false;
        }

        /* ──────────────── Helpers ─────────────── */

        /// <summary>
        /// Reads Jwt:RefreshTokenDays from config, defaulting to 30 if missing or malformed.
        /// </summary>
        private int RefreshDays()
        {
            // More defensive than int.Parse(...!): default to 30 if missing/malformed
            var s = _config["Jwt:RefreshTokenDays"];
            if (!int.TryParse(s, out var days) || days <= 0) days = 30;
            return days;
        }

        /// <summary>
        /// Builds a new <see cref="RefreshToken"/> entity with a hashed random token.
        /// The plain token string is NOT stored here — caller must overwrite TokenHash
        /// after SaveChanges to get the plain value for the cookie.
        ///
        /// Security note: Refresh tokens are currently bound only by expiry. To reduce the
        /// blast radius of token theft consider one or both of:
        ///   • Shorter lifetimes (e.g. 7 days instead of 30 for the persistent cookie).
        ///   • Client fingerprinting: store a hash of (User-Agent + IP + a server-side secret)
        ///     alongside the token and reject refreshes whose fingerprint hash doesn't match.
        ///     This is a soft check (UAs change on browser updates, IPs change on mobile), so
        ///     mismatches should trigger re-authentication rather than silent acceptance.
        ///   TODO: evaluate fingerprint binding before v2.0 launch.
        /// </summary>
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
            };
        }

        /// <summary>
        /// Writes the plain refresh token into the HttpOnly cookie and emits a
        /// diagnostic response header for debugging cookie-delivery issues.
        /// </summary>
        private void SetRefreshCookieFromPlain(string plain, DateTime expiresUtc)
        {
            _cookies.SetRefreshCookie(Response, plain, expiresUtc);
            Response.Headers["X-Refresh-Cookie-Attempt"] = $"ok; exp={expiresUtc:o}";
        }

        /// <summary>
        /// Revokes all active refresh tokens belonging to the same user as <paramref name="root"/>.
        /// Used as a security measure when token reuse (replay) is detected.
        /// </summary>
        private async Task RevokeTokenFamily(RefreshToken root)
        {
            var family = await _db.RefreshTokens
                .Where(r => r.UserId == root.UserId)
                .ToListAsync();

            foreach (var rt in family.Where(r => r.RevokedAtUtc == null))
            {
                rt.RevokedAtUtc = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
        }

        /* ───────────────── Auth Endpoints ────────────────     */

        // ---------- Register + Email Verification ----------

        /// <summary>
        /// Registers a new user account and sends a verification email.
        /// </summary>
        /// <param name="dto">User registration details including username, email, and password.</param>
        /// <returns>JWT access token and user information on success.</returns>
        /// <response code="200">Registration successful, returns token and user details.</response>
        /// <response code="400">Email already registered or validation failed.</response>
        /// <response code="429">Too many requests - rate limit exceeded.</response>
        [HttpPost("register")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        public async Task<IActionResult> Register([FromBody] RegisterUserDto dto)
        {
            // Check email uniqueness
            if (await _db.Users.AnyAsync(u => u.Email == dto.Email))
                return BadRequest(new { message = "Email is already registered." });

            // Auto-generate temporary username from email (user will set proper username in onboarding)
            // Using email as username triggers the onboarding flow
            var tempUsername = dto.Email.ToLower();

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

            var user = new User
            {
                Username = tempUsername, // Temporary - will be updated in onboarding
                Email = dto.Email,
                DisplayName = dto.DisplayName,
                PasswordHash = passwordHash,
                CreatedAt = DateTime.UtcNow,
                AuthProvider = "local",
                TrialStartUtc = DateTimeOffset.UtcNow,
                TrialEndUtc = DateTimeOffset.UtcNow.AddDays(7)
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            _log.LogInformation("New user registered. UserId={UserId}", user.Id);

            // Track registration conversion for Facebook Ads
            _ = _metaConversions.TrackRegistrationAsync(
                user.Email,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString());

            var token = Guid.NewGuid().ToString();
            var verificationToken = new VerificationToken
            {
                Id = Guid.NewGuid(),
                Token = token,
                UserId = user.Id,
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            };

            _db.VerificationTokens.Add(verificationToken);
            await _db.SaveChangesAsync();

            var frontendBaseUrl = _config["FrontendBaseUrl"] ?? "https://www.recipzee.com";
            var verificationLink = $"{frontendBaseUrl}/verify-email?token={token}";

            var subject = "Verify your Recipzee account";
            var body = $"Welcome to Recipzee! Click here to verify your email: {verificationLink}";

            await _emailSender.SendEmailAsync(user.Email, subject, body);

            var jwtToken = _jwt.GenerateToken(user);

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

        /// <summary>
        /// Confirms a user's email address by consuming a single-use verification token.
        /// </summary>
        /// <param name="token">GUID-format token from the verification link emailed on registration.</param>
        /// <returns>
        /// 200 on success; 400 if token is invalid, already used, or the user is not found.
        /// </returns>
        [HttpGet("verify-email")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> VerifyEmail([FromQuery] string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return BadRequest("Token is required.");

            // Atomic conditional update: marks IsUsed=1 only if currently 0.
            // This eliminates the TOCTOU race where two concurrent requests could
            // both pass the read-check-update sequence for the same token.
            var rows = await _db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE VerificationTokens SET IsUsed = 1 WHERE Token = {token} AND IsUsed = 0");

            if (rows == 0)
                return BadRequest("Invalid or already used token.");

            var verificationToken = await _db.VerificationTokens
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Token == token);

            if (verificationToken == null)
                return BadRequest("Invalid token.");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == verificationToken.UserId);
            if (user == null)
                return NotFound("User not found.");

            user.IsEmailVerified = true;
            await _db.SaveChangesAsync();

            _log.LogInformation("Email verified for UserId={UserId}", user.Id);

            return Ok("Email successfully verified.");
        }

        /// <summary>
        /// Issues a fresh verification token and resends the verification email.
        /// Safe to call multiple times — each call creates a new token; old tokens remain valid
        /// until their 24-hour expiry.
        /// </summary>
        /// <param name="dto">DTO containing the unverified email address.</param>
        /// <returns>200 on success; 404 if email not found; 400 if already verified.</returns>
        [HttpPost("resend-verification-email")]
        [AllowAnonymous]
        public async Task<IActionResult> ResendVerificationEmail([FromBody] ResendVerificationEmailDto dto)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null) return NotFound("User not found.");
            if (user.IsEmailVerified) return BadRequest("User is already verified.");

            var token = Guid.NewGuid().ToString();
            var verificationToken = new VerificationToken
            {
                UserId = user.Id,
                Token = token,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24),
                IsUsed = false
            };

            _db.VerificationTokens.Add(verificationToken);
            await _db.SaveChangesAsync();

            var verifyUrl = $"{_config["FrontendBaseUrl"]}/verify-email?token={token}";
            var subject = "Resend: Verify your Recipzee account";
            var body = $"Please click the following link to verify your email: {verifyUrl}";

            await _emailSender.SendEmailAsync(user.Email, subject, body);

            return Ok("Verification email resent.");
        }

        // ------------------ Login / Refresh ------------------

        /// <summary>
        /// Authenticates a user and returns a JWT access token.
        /// </summary>
        /// <param name="req">Login credentials (email and password).</param>
        /// <returns>Access token and user information on success.</returns>
        /// <response code="200">Login successful, returns token and user details.</response>
        /// <response code="401">Invalid email or password.</response>
        /// <response code="429">Too many requests - rate limit exceeded.</response>
        [HttpPost("login")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email);
            if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
                return Unauthorized(new { message = "Invalid credentials." });

            var accessToken = _jwt.GenerateToken(user);

            // Access token lifetime
            var accessMinutesStr = _config["Jwt:AccessTokenMinutes"];
            int accessMinutes = 15;
            if (int.TryParse(accessMinutesStr, out var parsed) && parsed > 0)
                accessMinutes = parsed;

            if (req.RememberMe)
            {
                // persistent-style access cookie
                _cookies.SetAccessCookie(Response, accessToken, DateTime.UtcNow.AddMinutes(accessMinutes));
            }
            else
            {
                // session-only access cookie (no explicit Expires)
                _cookies.SetAccessCookie(Response, accessToken, null);
            }

            // Always create refresh token - use shorter expiry for non-persistent sessions
            // This ensures users stay logged in on mobile even after closing the browser
            var refreshDays = req.RememberMe ? RefreshDays() : 7; // 7 days for non-persistent, 30 days for persistent

            var rt = BuildRefreshToken(user.Id);
            _db.RefreshTokens.Add(rt);
            await _db.SaveChangesAsync();

            // We need the plain token string — BuildRefreshToken only creates hash+DB entity.
            // Rebuild plain & hash pair now:
            var plain = TokenHashing.NewRandomToken();
            rt.TokenHash = TokenHashing.Hash(plain);
            rt.CreatedAtUtc = DateTime.UtcNow;
            rt.ExpiresAtUtc = DateTime.UtcNow.AddDays(refreshDays);

            _db.RefreshTokens.Update(rt);
            await _db.SaveChangesAsync();

            SetRefreshCookieFromPlain(plain, rt.ExpiresAtUtc);

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

        /// <summary>
        /// Issues a new JWT access token using the HttpOnly refresh-token cookie.
        /// The refresh token itself is NOT rotated — its expiry is fixed at creation time
        /// (no infinite sliding window). If the token is expired or revoked the cookie is cleared
        /// and 401 is returned, forcing the user to log in again.
        /// </summary>
        /// <returns>
        /// 200 with <c>{ token }</c> on success; 401 if refresh token is missing, invalid, revoked, or expired.
        /// Side effects: updates UserAgent and IpAddress on the token record for session auditing.
        /// </returns>
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

            if (token == null)
            {
                _cookies.ClearRefreshCookie(Response);
                return Unauthorized(new { message = "Invalid refresh token." });
            }

            if (token.RevokedAtUtc != null)
            {
                _cookies.ClearRefreshCookie(Response);
                return Unauthorized(new { message = "Refresh token has been revoked." });
            }

            // Hard 30-day cap: if expired, don’t extend, just kill it.
            if (token.ExpiresAtUtc <= DateTime.UtcNow)
            {
                token.RevokedAtUtc = DateTime.UtcNow;
                _db.RefreshTokens.Update(token);
                await _db.SaveChangesAsync();

                _cookies.ClearRefreshCookie(Response);
                return Unauthorized(new { message = "Refresh token expired." });
            }

            // ✅ Happy path: DO NOT extend ExpiresAtUtc (no infinite sliding).
            token.UserAgent = Request.Headers.UserAgent.ToString();
            token.IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            _db.RefreshTokens.Update(token);
            await _db.SaveChangesAsync();

            // Re-write cookie with existing expiry (no change)
            SetRefreshCookieFromPlain(cookiePlain, token.ExpiresAtUtc);

            var newAccess = _jwt.GenerateToken(token.User);

            var accessMinutesStr = _config["Jwt:AccessTokenMinutes"];
            int accessMinutes = 15;
            if (int.TryParse(accessMinutesStr, out var parsed) && parsed > 0)
                accessMinutes = parsed;

            _cookies.SetAccessCookie(Response, newAccess, DateTime.UtcNow.AddMinutes(accessMinutes));
            return Ok(new { token = newAccess });
        }

        // ------------------ Logout / Logout-all ------------------

        private CookieOptions GetCookieOptions(string? domain = null) => new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = "/",
            Domain = domain
        };

        private void ClearAllAuthCookies()
        {
            _cookies.ClearRefreshCookie(Response);
            _cookies.ClearAccessCookie(Response);

            var domains = new string?[] { null, ".recipzee.com", ".www.recipzee.com" };
            var cookieNames = new[] { "Recipzee.r", "recipzee.a" };

            foreach (var domain in domains)
            {
                var opts = GetCookieOptions(domain);
                foreach (var name in cookieNames)
                {
                    Response.Cookies.Delete(name, opts);
                }
            }
        }

        /// <summary>
        /// Logs the current user out by revoking the active refresh token and clearing
        /// all auth cookies (refresh + access) across all known domain variants.
        /// </summary>
        /// <returns>204 No Content.</returns>
        [Authorize]
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

            ClearAllAuthCookies();
            return NoContent();
        }

        /// <summary>
        /// Revokes ALL active refresh tokens for the authenticated user, effectively
        /// signing them out from every device and browser session simultaneously.
        /// </summary>
        /// <returns>204 No Content; 401 if userId claim is absent or malformed.</returns>
        [HttpPost("logout-all")]
        [Authorize]
        public async Task<IActionResult> LogoutAll()
        {
            var userIdClaim = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? User?.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var tokens = await _db.RefreshTokens
                .Where(r => r.UserId == userId && r.RevokedAtUtc == null)
                .ToListAsync();

            foreach (var t in tokens)
                t.RevokedAtUtc = DateTime.UtcNow;

            _db.RefreshTokens.UpdateRange(tokens);
            await _db.SaveChangesAsync();

            ClearAllAuthCookies();
            return NoContent();
        }

        // ------------------ Forgot / Reset Password ------------------

        /// <summary>
        /// Initiates the forgot-password flow by generating a 1-hour reset token and emailing it.
        /// Always returns 200 even when the email is not registered to prevent user enumeration.
        /// </summary>
        /// <param name="request">DTO containing the email address to send the reset link to.</param>
        /// <returns>200 OK (regardless of whether email exists in DB).</returns>
        [HttpPost("forgot-password")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto request)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
            if (user == null) return Ok();

            var token = Guid.NewGuid();
            var resetToken = new PasswordResetToken
            {
                UserId = user.Id,
                Token = token,
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                IsUsed = false
            };

            _db.PasswordResetTokens.Add(resetToken);
            await _db.SaveChangesAsync();

            var resetLink = $"{_config["FrontendBaseUrl"]}/reset-password?token={token}";
            await _emailSender.SendPasswordResetEmailAsync(user.Email, "Reset your Recipzee password",
                $"Click the link below to reset your password:\n\n{resetLink}");

            return Ok();
        }

        /// <summary>
        /// Completes the password reset by validating the token and updating the hash.
        /// The token is single-use and expires after 1 hour.
        /// </summary>
        /// <param name="request">DTO containing the reset token and the desired new password.</param>
        /// <returns>200 on success; 400 if token is invalid, expired, or user not found.</returns>
        [HttpPost("reset-password")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto request)
        {
            var tokenRecord = await _db.PasswordResetTokens
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.Token == request.Token && !t.IsUsed);

            if (tokenRecord == null || tokenRecord.ExpiresAt < DateTime.UtcNow)
                return BadRequest("Invalid or expired token.");

            var user = tokenRecord.User;
            if (user == null) return BadRequest("User not found.");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            tokenRecord.IsUsed = true;

            await _db.SaveChangesAsync();

            return Ok("Password has been reset successfully.");
        }

        // ------------------ External OAuth (Google) ------------------

        public class ExternalAuthRequest
        {
            public string Provider { get; set; } = ""; // "google"
            public string IdToken { get; set; } = "";  // Access token from provider
        }

        public class GoogleTokenInfo
        {
            public string? sub { get; set; }        // Google user ID
            public string? email { get; set; }
            public bool email_verified { get; set; }
            public string? name { get; set; }
            public string? picture { get; set; }
            public string? given_name { get; set; }
            public string? family_name { get; set; }
        }

        /// <summary>
        /// Authenticates with an external OAuth provider (currently only "google").
        /// Dispatches to the provider-specific handler; unsupported provider values return 400.
        /// Creates a new account if the user does not exist, or logs in if they do.
        /// New Google accounts also receive a 7-day trial (<see cref="HandleGoogleAuth"/>).
        /// </summary>
        /// <param name="req">
        /// <c>Provider</c>: the OAuth provider name (e.g. "google").
        /// <c>IdToken</c>: the OAuth access token obtained from the provider's SDK on the client.
        /// </param>
        /// <returns>
        /// 200 with <see cref="LoginResponse"/> (including <c>IsNewUser = true</c> for new accounts);
        /// 400 if provider is missing or unsupported; 401 if token verification fails.
        /// </returns>
        [HttpPost("external")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ExternalAuth([FromBody] ExternalAuthRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Provider))
                return BadRequest(new { message = "Provider is required." });

            return req.Provider.ToLower() switch
            {
                "google" => await HandleGoogleAuth(req.IdToken),
                _ => BadRequest(new { message = $"Unsupported provider: {req.Provider}" })
            };
        }

        /// <summary>
        /// Handles the Google OAuth flow:
        /// <list type="number">
        ///   <item>Verifies the access token audience against <c>Google:ClientId</c> config using
        ///         Google's /tokeninfo endpoint (guards against tokens issued for other apps).</item>
        ///   <item>Fetches the user's profile from Google's /oauth2/v3/userinfo endpoint.</item>
        ///   <item>Looks up the user by <c>GoogleId</c> (sub claim).
        ///         If not found, tries by email to link an existing local account.
        ///         If no match, creates a new user with <c>TrialStartUtc</c> and <c>TrialEndUtc</c> set
        ///         (7-day trial) and returns <c>IsNewUser = true</c> for analytics tracking.</item>
        ///   <item>Calls <see cref="GenerateAuthResponse"/> with <c>rememberMe = true</c>
        ///         (Google sessions always persist via refresh token).</item>
        /// </list>
        /// The username is temporarily set to the user's email address; the frontend detects
        /// the '@' character and routes the user to the onboarding flow to choose a real username.
        /// </summary>
        private async Task<IActionResult> HandleGoogleAuth(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return BadRequest(new { message = "Access token is required." });

            var expectedClientId = _config["Google:ClientId"];

            GoogleTokenInfo? tokenInfo;
            try
            {
                using var httpClient = _httpClientFactory.CreateClient();

                // 1. Verify token audience — ensures token was issued for OUR app
                if (!string.IsNullOrWhiteSpace(expectedClientId))
                {
                    var tokenInfoResponse = await httpClient.GetAsync(
                        $"https://oauth2.googleapis.com/tokeninfo?access_token={Uri.EscapeDataString(accessToken)}");

                    if (!tokenInfoResponse.IsSuccessStatusCode)
                        return Unauthorized(new { message = "Invalid Google token." });

                    var tokenInfoJson = await tokenInfoResponse.Content.ReadAsStringAsync();
                    var tokenInfoDoc = JsonSerializer.Deserialize<JsonElement>(tokenInfoJson);

                    var aud = tokenInfoDoc.TryGetProperty("aud", out var audProp) ? audProp.GetString() : null;
                    var azp = tokenInfoDoc.TryGetProperty("azp", out var azpProp) ? azpProp.GetString() : null;

                    if (aud != expectedClientId && azp != expectedClientId)
                        return Unauthorized(new { message = "Token was not issued for this application." });
                }

                // 2. Fetch user info
                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                var response = await httpClient.GetAsync("https://www.googleapis.com/oauth2/v3/userinfo");

                if (!response.IsSuccessStatusCode)
                    return Unauthorized(new { message = "Invalid Google token." });

                var json = await response.Content.ReadAsStringAsync();
                tokenInfo = JsonSerializer.Deserialize<GoogleTokenInfo>(json);

                if (tokenInfo == null || string.IsNullOrEmpty(tokenInfo.sub))
                    return Unauthorized(new { message = "Could not verify Google token." });
            }
            catch (Exception ex)
            {
                return Unauthorized(new { message = "Failed to verify Google token.", error = ex.Message });
            }

            // Find or create user
            var user = await _db.Users.FirstOrDefaultAsync(u => u.GoogleId == tokenInfo.sub);

            if (user == null)
            {
                // Check if email is already registered with different auth method
                user = await _db.Users.FirstOrDefaultAsync(u => u.Email == tokenInfo.email);

                if (user != null)
                {
                    // Link Google account to existing user
                    user.GoogleId = tokenInfo.sub;
                    if (string.IsNullOrEmpty(user.ProfileImageUrl) && !string.IsNullOrEmpty(tokenInfo.picture))
                        user.ProfileImageUrl = tokenInfo.picture;
                    await _db.SaveChangesAsync();
                }
                else
                {
                    // Create new user
                    // Use email as temporary username to trigger onboarding flow
                    // (username containing '@' triggers needsOnboarding check in frontend)
                    var tempUsername = (tokenInfo.email ?? "").ToLower();
                    if (string.IsNullOrEmpty(tempUsername))
                    {
                        tempUsername = GenerateUniqueUsername(tokenInfo.name ?? "user");
                    }

                    user = new User
                    {
                        GoogleId = tokenInfo.sub,
                        Email = tokenInfo.email ?? "",
                        Username = tempUsername,
                        DisplayName = tokenInfo.name,
                        ProfileImageUrl = tokenInfo.picture,
                        IsEmailVerified = tokenInfo.email_verified,
                        AuthProvider = "google",
                        PasswordHash = "", // No password for OAuth users
                        CreatedAt = DateTime.UtcNow,
                        TrialStartUtc = DateTimeOffset.UtcNow,
                        TrialEndUtc = DateTimeOffset.UtcNow.AddDays(7)
                    };

                    _db.Users.Add(user);
                    await _db.SaveChangesAsync();

                    return await GenerateAuthResponse(user, rememberMe: true, isNewUser: true);
                }
            }

            return await GenerateAuthResponse(user, rememberMe: true);
        }

        /// <summary>
        /// Generates a URL-safe unique username derived from a display name.
        /// Strips non-alphanumeric characters, enforces 3–15 char length,
        /// and appends an incrementing suffix (or a random hex suffix after 9999 attempts)
        /// to guarantee uniqueness within the DB.
        /// </summary>
        private string GenerateUniqueUsername(string baseName)
        {
            // Clean the base name
            var clean = new string(baseName
                .ToLower()
                .Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '.')
                .ToArray());

            if (clean.Length < 3) clean = "user";
            if (clean.Length > 15) clean = clean.Substring(0, 15);

            // Check if username exists, append numbers if needed
            var username = clean;
            var counter = 1;

            while (_db.Users.Any(u => u.Username == username))
            {
                username = $"{clean}{counter}";
                counter++;
                if (counter > 9999) username = $"{clean}{Guid.NewGuid().ToString("N").Substring(0, 6)}";
            }

            return username;
        }

        /// <summary>
        /// Builds the final authentication response shared by both the Google OAuth path and
        /// any future OAuth providers. When <paramref name="rememberMe"/> is true, a refresh
        /// token is persisted and the HttpOnly cookie is set with a fixed expiry. When false,
        /// only the access cookie is written (session-duration).
        /// </summary>
        /// <param name="user">The resolved or newly created user entity.</param>
        /// <param name="rememberMe">
        /// True for OAuth flows (always persistent) or when the login form checkbox is ticked.
        /// </param>
        /// <param name="isNewUser">
        /// True for brand-new accounts. Propagated to <see cref="LoginResponse.IsNewUser"/>
        /// so the frontend fires analytics events (GA4 / Meta Pixel) only on first sign-up.
        /// </param>
        private async Task<IActionResult> GenerateAuthResponse(User user, bool rememberMe, bool isNewUser = false)
        {
            var accessToken = _jwt.GenerateToken(user);

            var accessMinutesStr = _config["Jwt:AccessTokenMinutes"];
            int accessMinutes = 15;
            if (int.TryParse(accessMinutesStr, out var parsed) && parsed > 0)
                accessMinutes = parsed;

            if (rememberMe)
            {
                _cookies.SetAccessCookie(Response, accessToken, DateTime.UtcNow.AddMinutes(accessMinutes));

                var rt = BuildRefreshToken(user.Id);
                _db.RefreshTokens.Add(rt);
                await _db.SaveChangesAsync();

                var plain = TokenHashing.NewRandomToken();
                rt.TokenHash = TokenHashing.Hash(plain);
                rt.CreatedAtUtc = DateTime.UtcNow;
                rt.ExpiresAtUtc = DateTime.UtcNow.AddDays(RefreshDays());

                _db.RefreshTokens.Update(rt);
                await _db.SaveChangesAsync();

                SetRefreshCookieFromPlain(plain, rt.ExpiresAtUtc);
            }
            else
            {
                _cookies.SetAccessCookie(Response, accessToken, null);
            }

            return Ok(new LoginResponse
            {
                Token = accessToken,
                IsNewUser = isNewUser,
                User = new
                {
                    id = user.Id,
                    email = user.Email,
                    username = user.Username,
                    displayName = user.DisplayName,
                    profileImageUrl = user.ProfileImageUrl,
                    isEmailVerified = user.IsEmailVerified,
                    authProvider = user.AuthProvider
                }
            });
        }

        // ------------------ Change Password ------------------

        public class ChangePasswordRequest
        {
            public string CurrentPassword { get; set; } = "";
            public string NewPassword { get; set; } = "";
        }

        /// <summary>
        /// Changes the authenticated user's password.
        /// </summary>
        [HttpPost("change-password")]
        [Authorize]
        [EnableRateLimiting("auth")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(userId, out var uid))
                return Unauthorized("Invalid token.");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid);
            if (user == null)
                return Unauthorized("User not found.");

            // Users who signed up with OAuth don't have a password
            if (user.AuthProvider != "local" && string.IsNullOrEmpty(user.PasswordHash))
                return BadRequest("Cannot change password for accounts created with social login. Please use the 'Set Password' option instead.");

            // Verify current password
            if (!BCrypt.Net.BCrypt.Verify(req.CurrentPassword, user.PasswordHash))
                return BadRequest("Current password is incorrect.");

            // Validate new password
            if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 8)
                return BadRequest("New password must be at least 8 characters.");

            // Update password
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
            await _db.SaveChangesAsync();

            // Revoke all refresh tokens for security (force re-login on other devices)
            var tokens = await _db.RefreshTokens
                .Where(r => r.UserId == uid && r.RevokedAtUtc == null)
                .ToListAsync();
            foreach (var t in tokens)
                t.RevokedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new { message = "Password changed successfully. Please log in again on other devices." });
        }

        // ------------------ Set Password (for OAuth users) ------------------

        public class SetPasswordRequest
        {
            public string NewPassword { get; set; } = "";
        }

        /// <summary>
        /// Sets a password for users who signed up with OAuth (Google/Facebook).
        /// </summary>
        [HttpPost("set-password")]
        [Authorize]
        [EnableRateLimiting("auth")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> SetPassword([FromBody] SetPasswordRequest req)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(userId, out var uid))
                return Unauthorized("Invalid token.");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid);
            if (user == null)
                return Unauthorized("User not found.");

            // Only allow if user signed up with OAuth and doesn't have a password
            if (user.AuthProvider == "local")
                return BadRequest("Use 'Change Password' to update your existing password.");

            if (!string.IsNullOrEmpty(user.PasswordHash))
                return BadRequest("Password already set. Use 'Change Password' to update it.");

            // Validate new password
            if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 8)
                return BadRequest("Password must be at least 8 characters.");

            // Set password
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Password set successfully. You can now log in with email and password." });
        }

        // ------------------ Change Email ------------------

        public class ChangeEmailRequest
        {
            public string NewEmail { get; set; } = "";
            public string Password { get; set; } = "";
        }

        /// <summary>
        /// Initiates an email change by sending verification to the new email.
        /// </summary>
        [HttpPost("change-email")]
        [Authorize]
        [EnableRateLimiting("auth")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> ChangeEmail([FromBody] ChangeEmailRequest req)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(userId, out var uid))
                return Unauthorized("Invalid token.");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid);
            if (user == null)
                return Unauthorized("User not found.");

            // Normalize email
            var newEmail = req.NewEmail?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(newEmail) || !newEmail.Contains("@"))
                return BadRequest("Please provide a valid email address.");

            // Check if email is already taken
            var emailTaken = await _db.Users.AnyAsync(u => u.Email == newEmail && u.Id != uid);
            if (emailTaken)
                return BadRequest("This email is already registered to another account.");

            // For local accounts, verify password
            if (user.AuthProvider == "local" || !string.IsNullOrEmpty(user.PasswordHash))
            {
                if (string.IsNullOrEmpty(req.Password))
                    return BadRequest("Password is required to change email.");

                if (!BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
                    return BadRequest("Password is incorrect.");
            }

            // Store pending email and create verification token
            user.PendingEmail = newEmail;
            user.PendingEmailToken = Guid.NewGuid().ToString();
            user.PendingEmailTokenExpiry = DateTime.UtcNow.AddHours(24);
            await _db.SaveChangesAsync();

            // Send verification email to new address
            var verifyUrl = $"{_config["FrontendBaseUrl"]}/verify-email-change?token={user.PendingEmailToken}";
            var subject = "Verify your new email address - Recipzee";
            var body = $@"
                <p>You requested to change your Recipzee account email to this address.</p>
                <p>Please click the link below to verify this email address:</p>
                <p><a href=""{verifyUrl}"">{verifyUrl}</a></p>
                <p>This link expires in 24 hours.</p>
                <p>If you didn't request this change, you can safely ignore this email.</p>
            ";

            await _emailSender.SendEmailAsync(newEmail, subject, body);

            return Ok(new { message = "Verification email sent to your new address. Please check your inbox." });
        }

        /// <summary>
        /// Confirms the email change after user clicks verification link.
        /// </summary>
        [HttpGet("verify-email-change")]
        [AllowAnonymous]
        public async Task<IActionResult> VerifyEmailChange([FromQuery] string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return BadRequest("Token is required.");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.PendingEmailToken == token);
            if (user == null)
                return BadRequest("Invalid or expired token.");

            if (user.PendingEmailTokenExpiry < DateTime.UtcNow)
            {
                // Clear expired pending email
                user.PendingEmail = null;
                user.PendingEmailToken = null;
                user.PendingEmailTokenExpiry = null;
                await _db.SaveChangesAsync();
                return BadRequest("Token has expired. Please request a new email change.");
            }

            if (string.IsNullOrEmpty(user.PendingEmail))
                return BadRequest("No pending email change found.");

            // Check again if email was taken in the meantime
            var emailTaken = await _db.Users.AnyAsync(u => u.Email == user.PendingEmail && u.Id != user.Id);
            if (emailTaken)
            {
                user.PendingEmail = null;
                user.PendingEmailToken = null;
                user.PendingEmailTokenExpiry = null;
                await _db.SaveChangesAsync();
                return BadRequest("This email has been registered by another user.");
            }

            // Update email
            var oldEmail = user.Email;
            user.Email = user.PendingEmail;
            user.IsEmailVerified = true;  // New email is now verified
            user.PendingEmail = null;
            user.PendingEmailToken = null;
            user.PendingEmailTokenExpiry = null;
            await _db.SaveChangesAsync();

            // Send notification to old email
            await _emailSender.SendEmailAsync(oldEmail, "Your Recipzee email was changed",
                $"Your Recipzee account email has been changed to {user.Email}. If you didn't make this change, please contact support immediately.");

            return Ok("Email changed successfully.");
        }
    }
}
