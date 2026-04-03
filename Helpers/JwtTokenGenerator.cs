using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Platerly.Api.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Platerly.Api.Helpers
{
    public class JwtTokenGenerator
    {
        private readonly IConfiguration _config;

        public JwtTokenGenerator(IConfiguration config)
        {
            _config = config;
        }

        /// <summary>
        /// Generates a signed JWT access token for the given user.
        ///
        /// PII note: the token payload includes an <c>email</c> claim (required by some OIDC consumers).
        /// Never log the raw token string — it embeds the user's email address. Log only
        /// <c>user.Id</c> if you need to emit diagnostic information about token issuance.
        /// </summary>
        public string GenerateToken(User user)
        {
            // --- read & validate signing key ---
            var keyStr = _config["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(keyStr))
                throw new InvalidOperationException("JWT signing key (Jwt:Key) is not configured.");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyStr));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            // --- issuer/audience (optional but recommended) ---
            var issuer = _config["Jwt:Issuer"] ?? "Recipzee";
            var audience = _config["Jwt:Audience"] ?? "RecipzeeUsers";

            // --- access token lifetime with robust fallback ---
            // Prefer new key; fall back to legacy; default 60 minutes.
            var minutesStr =
                _config["Jwt:AccessTokenMinutes"] ??
                _config["Jwt:ExpiresInMinutes"];

            if (!int.TryParse(minutesStr, out var minutes) || minutes <= 0)
                minutes = 60;

            // --- claims ---
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
                new("username", user.Username ?? string.Empty),
                new(ClaimTypes.Role, user.Role.ToString()),
            };

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.AddMinutes(minutes),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
