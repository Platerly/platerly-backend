using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Services;
using static Platerly.Api.Services.AllergenResolver;

namespace Platerly.Api.Controllers
{
    [ApiController]
    [Route("share")]
    public class ShareController : ControllerBase
    {
        private readonly PlaterlyDbContext _db;
        private readonly IConfiguration _config;
        private readonly IAllergenResolver _allergenResolver;

        public ShareController(PlaterlyDbContext db, IConfiguration config, IAllergenResolver allergenResolver)
        {
            _db = db;
            _config = config;
            _allergenResolver = allergenResolver;
        }

        // ----------------- helpers (singular/plural tolerant) -----------------
        private static string Root(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant();
            if (s.EndsWith("ies")) return s[..^3] + "y";  // berries -> berry
            if (s.EndsWith("es")) return s[..^2];        // tomatoes -> tomato (rough)
            if (s.EndsWith("s")) return s[..^1];        // almonds -> almond
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

        // GET /share/recipe/{id}
        [HttpGet("recipe/{id:guid}")]
        [ResponseCache(Duration = 21600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IActionResult> ShareRecipe([FromRoute] Guid id)
        {
            // Pull the recipe + tag names + ingredient names in one query
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

            if (r is null)
                return NotFound("Recipe not found");

            // ---------- Derive potential allergens (Nickel map; medium+ like Feed) ----------
            var ingNamesLower = r.IngredientNames
                .Select(n => (n ?? string.Empty).Trim().ToLowerInvariant())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .ToList();

            // Use resolver like Feed does (seed with "nickel", then take medium+)
            var expandedWithSeverity =
                (_allergenResolver as AllergenResolver)?.ExpandWithSeverity(new[] { "nickel" })
                ?? new List<(string term, Severity severity)>();

            // Build severity map for ingredients by loose matching
            var matchedSeverities = new Dictionary<string, string>(); // ingredient(lower) -> "high|medium|low"
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
                .Select(k => k)   // already lower-case names
                .Distinct()
                .Take(3)
                .ToList();

            // --------------------------
            // 🌐 Base URL + share setup
            // --------------------------
            var publicBase = _config["PublicBaseUrl"]?.TrimEnd('/') ?? "https://www.platerly.com";
            var site = "Platerly";
            var canonicalUrl = $"{publicBase}/recipe/{r.Id}";
            var shareImage = string.IsNullOrWhiteSpace(r.ImageUrl)
                    ? $"{publicBase}/og-preview.png"
                    : $"{publicBase}/og/recipe/{r.Id}.png";


            // --------------------------
            // 🕒 Compose readable preview text
            // --------------------------
            string? timePart =
                (r.PrepTimeMinutes is > 0 ? $"{r.PrepTimeMinutes} min prep" : null) is string p &&
                (r.CookTimeMinutes is > 0 ? $"{r.CookTimeMinutes} min cook" : null) is string c
                    ? $"{p}, {c}"
                    : (r.PrepTimeMinutes is > 0 ? $"{r.PrepTimeMinutes} min prep"
                       : (r.CookTimeMinutes is > 0 ? $"{r.CookTimeMinutes} min cook" : null));

            var tagList = (r.TagNames ?? new List<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .Select(t => $"#{t}");

            string desc = string.Join(" · ", new[]
            {
                r.Difficulty.HasValue ? $"Difficulty: {r.Difficulty.Value}" : null, // Fix: Handle nullable Difficulty
                timePart,
                tagList.Any() ? string.Join(" ", tagList) : null,
                allergenList.Any() ? $"⚠️ Allergens: {string.Join(", ", allergenList)}" : null
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

            if (desc.Length > 240) desc = desc[..240] + "…";

            // --------------------------
            // 🧱 Minimal OpenGraph HTML
            // --------------------------
            string html = $@"<!doctype html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<title>{E($"{r.Title} — {site}")}</title>
<link rel=""canonical"" href=""{canonicalUrl}"" />

<!-- Open Graph -->
<meta property=""og:type"" content=""article"">
<meta property=""og:site_name"" content=""{site}"">
<meta property=""og:title"" content=""{E(r.Title)}"">
<meta property=""og:description"" content=""{E(desc)}"">
<meta property=""og:url"" content=""{canonicalUrl}"">
<meta property=""og:image"" content=""{shareImage}"">
<meta property=""og:image:alt"" content=""{E($"{r.Title} on {site}")}"">

<!-- Twitter -->
<meta name=""twitter:card"" content=""summary_large_image"">
<meta name=""twitter:title"" content=""{E(r.Title)}"">
<meta name=""twitter:description"" content=""{E(desc)}"">
<meta name=""twitter:image"" content=""{shareImage}"">
<meta name=""twitter:site"" content=""@platerlyinc"">

<!-- Quick redirect for humans -->
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

        // GET /og/recipe/{id}.png  (kept simple; still redirects to the recipe image)
        [HttpGet("/og/recipe/{id:guid}.png")]
        [ResponseCache(Duration = 21600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IActionResult> OgRecipePng([FromRoute] Guid id)
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
