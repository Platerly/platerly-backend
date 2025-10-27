using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Services;
using static Platerly.Api.Services.AllergenResolver;
using Microsoft.Extensions.Logging;

namespace Platerly.Api.Controllers
{
    [ApiController]
    [Route("api/[Controller]")]
    public class ShareController : ControllerBase
    {
        private readonly PlaterlyDbContext _db;
        private readonly IConfiguration _config;
        private readonly IAllergenResolver _allergenResolver;
        private readonly ILogger<ShareController> _logger;

        public ShareController(PlaterlyDbContext db, IConfiguration config, IAllergenResolver allergenResolver, ILogger<ShareController> logger)
        {
            _db = db;
            _config = config;
            _allergenResolver = allergenResolver;
            _logger = logger;
        }

        private static string Root(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant();
            if (s.EndsWith("ies")) return s[..^3] + "y";
            if (s.EndsWith("es")) return s[..^2];
            if (s.EndsWith("s")) return s[..^1];
            return s;
        }

        private static bool LooseMatch(string ingLower, string termLower)
        {
            if (string.IsNullOrWhiteSpace(ingLower) || string.IsNullOrWhiteSpace(termLower)) return false;
            if (ingLower.Contains(termLower) || termLower.Contains(ingLower)) return true;
            var rIng = Root(ingLower);
            var rTerm = Root(termLower);
            return rIng.Contains(rTerm) || rTerm.Contains(rIng);
        }

        // --- HEAD so the client-side HEAD ping doesn't 405 ---
        [HttpHead("recipe/{id:guid}")]
        public Task<IActionResult> ShareRecipeHead([FromRoute] Guid id) => ShareRecipe(id);

        [HttpGet("recipe/{id:guid}")]
        [ResponseCache(Duration = 21600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IActionResult> ShareRecipe([FromRoute] Guid id)
        {
            try
            {
                var r = await _db.Recipes
                    .Where(x => x.Id == id)
                    .Select(x => new
                    {
                        x.Id,
                        x.Title,
                        x.Description,
                        x.ImageUrl,
                        x.Difficulty,
                        PrepTimeMinutes = x.PrepTimeMinutes,
                        CookTimeMinutes = x.CookTimeMinutes,
                        TagNames = x.RecipeTags.Select(rt => rt.Tag.Name).ToList(),
                        IngredientNames = x.Ingredients
                            .Select(i => i.GlobalIngredient != null ? i.GlobalIngredient.Name : "")
                            .ToList()
                    })
                    .FirstOrDefaultAsync();

                if (r == null)
                {
                    _logger.LogWarning("Recipe not found for ID: {Id}", id);
                    return NotFound("Recipe not found");
                }

                var ingNamesLower = r.IngredientNames
                    .Select(n => (n ?? string.Empty).Trim().ToLowerInvariant())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct()
                    .ToList();

                var expandedWithSeverity = (_allergenResolver as AllergenResolver)?.ExpandWithSeverity(new[] { "nickel" })
                    ?? new List<(string term, Severity severity)>();

                var matchedSeverities = new Dictionary<string, string>();
                foreach (var ing in ingNamesLower)
                {
                    Severity? best = null;
                    foreach (var a in expandedWithSeverity)
                    {
                        var term = (a.term ?? "").ToLowerInvariant();
                        if (LooseMatch(ing, term))
                        {
                            if (best == null || a.severity > best) best = a.severity;
                        }
                    }
                    if (best != null && best.Value >= Severity.Medium)
                        matchedSeverities[ing] = best.Value.ToString().ToLowerInvariant();
                }

                var allergenList = matchedSeverities.Keys
                    .Distinct()
                    .Take(3)
                    .ToList();

                // IMPORTANT: This must be your public web origin (NOT the API hostname)
                var publicBase = _config["PublicBaseUrl"]?.TrimEnd('/') ?? "https://www.platerly.com";
                var site = "Platerly";
                var canonicalUrl = $"{publicBase}/recipe/{r.Id}";

                // Keep image on the public domain; SWA will rewrite /og/recipe/* → API
                var shareImage = $"{publicBase}/og/recipe/{r.Id}.png";

                string? timePart = (r.PrepTimeMinutes > 0 ? $"{r.PrepTimeMinutes} min prep" : null) is string p &&
                                   (r.CookTimeMinutes > 0 ? $"{r.CookTimeMinutes} min cook" : null) is string c
                    ? $"{p}, {c}"
                    : (r.PrepTimeMinutes > 0 ? $"{r.PrepTimeMinutes} min prep" : (r.CookTimeMinutes > 0 ? $"{r.CookTimeMinutes} min cook" : null));

                var tagList = (r.TagNames ?? new List<string>())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .Select(t => $"#{t}");

                string desc = string.Join(" · ", new[]
                {
                    r.Difficulty.HasValue ? $"Difficulty: {r.Difficulty.Value}" : null,
                    timePart,
                    tagList.Any() ? string.Join(" ", tagList) : null,
                    allergenList.Any() ? $"⚠️ Allergens: {string.Join(", ", allergenList)}" : null
                }.Where(s => !string.IsNullOrWhiteSpace(s)));

                if (desc.Length > 240) desc = desc[..240] + "…";

                string html = $@"<!doctype html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<title>{E($"{r.Title} — {site}")}</title>
<link rel=""canonical"" href=""{canonicalUrl}"" />

<meta property=""og:type"" content=""article"">
<meta property=""og:site_name"" content=""{site}"">
<meta property=""og:title"" content=""{E(r.Title)}"">
<meta property=""og:description"" content=""{E(desc)}"">
<meta property=""og:url"" content=""{canonicalUrl}"">
<meta property=""og:image"" content=""{shareImage}"">
<meta property=""og:image:alt"" content=""{E($"{r.Title} on {site}")}"">

<meta name=""twitter:card"" content=""summary_large_image"">
<meta name=""twitter:title"" content=""{E(r.Title)}"">
<meta name=""twitter:description"" content=""{E(desc)}"">
<meta name=""twitter:image"" content=""{shareImage}"">
<meta name=""twitter:url"" content=""{canonicalUrl}"">
<meta name=""twitter:domain"" content=""platerly.com"">
<meta name=""twitter:site"" content=""@platerlyinc"">

<meta http-equiv=""refresh"" content=""0; url={canonicalUrl}"">
<style>
  html,body{{background:#081615;color:#cbe3df;font-family:system-ui,-apple-system,Segoe UI,Roboto,Ubuntu,Arial,sans-serif}}
  .wrap{{display:flex;min-height:100vh;align-items:center;justify-content:center;text-align:center;padding:24px}}
  a{{color:#5EEAD4}}
</style>
</head>
<body>
  <div class=""wrap"">
    <div>
      <h1>{E(r.Title)}</h1>
      <p>Opening recipe… If you are not redirected, <a href=""{canonicalUrl}"">tap here</a>.</p>
    </div>
  </div>
</body>
</html>";

                return Content(html, "text/html; charset=utf-8");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing share request for recipe ID: {Id}", id);
                return NotFound("An error occurred while processing the share request.");
            }
        }

        // Public path (root) — crawlers will request https://www.platerly.com/og/recipe/{id}.png
        [HttpGet("/og/recipe/{id:guid}.png")]
        [ResponseCache(Duration = 21600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IActionResult> OgRecipePngRoot([FromRoute] Guid id)
        {
            var imageUrl = await _db.Recipes
                .Where(r => r.Id == id)
                .Select(r => r.ImageUrl)
                .FirstOrDefaultAsync();

            var publicBase = _config["PublicBaseUrl"]?.TrimEnd('/') ?? "https://www.platerly.com";
            var fallback = $"{publicBase}/og-preview.png";
            return Redirect(string.IsNullOrWhiteSpace(imageUrl) ? fallback : imageUrl);
        }

        // API-scoped alias — lets SWA rewrite /og/recipe/* → /api/share/og/recipe/*
        [HttpGet("og/recipe/{id:guid}.png")]
        [ResponseCache(Duration = 21600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IActionResult> OgRecipePngApi([FromRoute] Guid id)
        {
            var imageUrl = await _db.Recipes
                .Where(r => r.Id == id)
                .Select(r => r.ImageUrl)
                .FirstOrDefaultAsync();

            var publicBase = _config["PublicBaseUrl"]?.TrimEnd('/') ?? "https://www.platerly.com";
            var fallback = $"{publicBase}/og-preview.png";
            return Redirect(string.IsNullOrWhiteSpace(imageUrl) ? fallback : imageUrl);
        }

        private static string E(string s) => WebUtility.HtmlEncode(s);
    }
}
