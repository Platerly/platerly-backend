using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Extensions;
using Platerly.Api.Services.Allergy;
using System.Security.Claims;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2C Allergen Profile controller. Manages the authenticated user's allergen preferences
    /// and returns per-recipe allergen hit details.
    ///
    /// Base route: /api/Allergy
    /// Auth requirement: GET /all is unauthenticated; all other endpoints require [Authorize] (JWT).
    ///
    /// Endpoints:
    ///   GET  /all           — returns the full list of supported allergens (reference data).
    ///   GET  /my-allergies  — returns the current user's enabled allergen names as a plain
    ///                         <c>string[]</c> (lowercase), suitable for direct use in the
    ///                         frontend allergen filter without further transformation.
    ///   PUT  /update        — replaces the user's allergen profile atomically: removes all
    ///                         existing <c>UserAllergy</c> rows and inserts the new set.
    ///   GET  /hits          — returns per-ingredient allergen hits for a recipe, gated by the
    ///                         weekly scan quota (see <see cref="IAllergenScanService"/>).
    ///                         Returns HTTP 402 on quota exhaustion.
    ///
    /// Scan quota (GET /hits):
    ///   Free users may scan up to 3 unique recipes per calendar week (Monday–Sunday UTC).
    ///   The <see cref="IAllergenScanService.TryConsumeOrCountScanAsync"/> call is shared with
    ///   the <c>GET /api/Recipe/{id}/alerts</c> endpoint — the unique index on
    ///   <c>(UserId, RecipeId, WeekStartDate)</c> prevents double-counting the same recipe
    ///   regardless of which endpoint is called first.
    ///   Premium and trial users bypass the gate entirely.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class AllergyController : ControllerBase
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<AllergyController> _log;
        private readonly IAllergyAlertService _alerts;

        private readonly IAllergenScanService _scanService;

        public AllergyController(
            PlaterlyDbContext db,
            ILogger<AllergyController> log,
            IAllergyAlertService alerts,
            IAllergenScanService scanService)
        {
            _db = db;
            _log = log;
            _alerts = alerts;
            _scanService = scanService;
        }

        /// <summary>
        /// Returns the complete list of supported allergens from the <c>Allergies</c> reference table,
        /// ordered alphabetically. Used to populate the allergen selection UI.
        /// </summary>
        // GET: /api/Allergy/all
        [HttpGet("all")]
        public async Task<IActionResult> GetAllAllergies()
        {
            var allergies = await _db.Allergies
                .OrderBy(a => a.Name)
                .ToListAsync();

            return Ok(allergies);
        }

        /// <summary>
        /// Returns the authenticated user's enabled allergen names as a plain lowercase <c>string[]</c>.
        ///
        /// Returns allergen names in lowercase (not IDs) because the frontend allergen-matching
        /// logic compares against lowercase ingredient names directly without a lookup step.
        /// Only allergens where <c>IsEnabled = true</c> are returned.
        /// </summary>
        /// <returns>200 with <c>string[]</c> of allergen names; 401 if not authenticated.</returns>
        // GET: /api/Allergy/my-allergies
        [Authorize]
        [HttpGet("my-allergies")]
        public async Task<IActionResult> GetMyAllergies()
        {
            if (!User.TryGetUserId(out var userId))
                return Unauthorized();

            var allergyNames = await _db.UserAllergies
                .Where(ua => ua.UserId == userId && ua.IsEnabled)
                .Include(ua => ua.Allergy)
                .OrderBy(ua => ua.Allergy!.Name)
                .Select(ua => ua.Allergy!.Name.ToLower())
                .ToListAsync();

            return Ok(allergyNames);
        }

        /// <summary>
        /// Atomically replaces the authenticated user's allergen profile.
        /// Removes all existing <c>UserAllergy</c> rows for the user, then inserts a new row
        /// for each distinct allergen ID in the request body.
        /// </summary>
        /// <param name="newAllergyIds">
        /// List of allergen GUIDs to enable. Duplicates are ignored (<c>Distinct()</c>).
        /// An empty list removes all allergens (valid use case for clearing the profile).
        /// </param>
        /// <returns>200 on success; 401 if not authenticated.</returns>
        // PUT: /api/Allergy/update
        [Authorize]
        [HttpPut("update")]
        public async Task<IActionResult> UpdateUserAllergies([FromBody] List<Guid> newAllergyIds)
        {
            if (!User.TryGetUserId(out var userId))
                return Unauthorized();

            var existing = await _db.UserAllergies
                .Where(ua => ua.UserId == userId)
                .ToListAsync();

            _db.UserAllergies.RemoveRange(existing);

            foreach (var allergyId in newAllergyIds.Distinct())
            {
                _db.UserAllergies.Add(new Models.UserAllergy
                {
                    UserId = userId,
                    AllergyId = allergyId,
                    IsEnabled = true
                });
            }

            await _db.SaveChangesAsync();
            return Ok("Allergies updated.");
        }

        // ✅ Replacement for the old stored-proc endpoint:
        // GET: /api/Allergy/hits?recipeId={GUID}
        // Returns the SAME shape your FE can use to highlight ingredients.
        public record AllergenHitDto(
            Guid IngredientId,
            string IngredientLabel,
            string? Quantity,
            Guid GlobalIngredientId,
            string AllergenType,
            string HitSource,
            string HitSeverity,
            bool MayContain,
            bool CrossContactRisk,
            bool ProcessingAid
        );

        /// <summary>
        /// Returns per-ingredient allergen hits for a recipe against the authenticated user's allergen profile.
        ///
        /// Scan quota gate:
        ///   Before running the alert query, <see cref="IAllergenScanService.TryConsumeOrCountScanAsync"/>
        ///   is called. For free users this consumes one weekly scan (or counts the recipe as already scanned
        ///   if seen this week). If the weekly limit (3) is already reached, returns HTTP 402 with
        ///   <c>{ error, scansUsed, scansLimit, weekResetsAt }</c> so the frontend can render the
        ///   blur overlay and upgrade prompt.
        ///
        /// Response shape: flat list of <see cref="AllergenHitDto"/> rows — one row per
        /// (ingredient × allergen type) combination, including quantity and metadata for
        /// ingredient-level highlighting in the recipe view.
        /// </summary>
        /// <param name="recipeId">The recipe to check against the user's allergen profile.</param>
        /// <returns>
        /// 200 with <c>IEnumerable&lt;AllergenHitDto&gt;</c>;
        /// 401 if not authenticated;
        /// 402 with scan status payload if the weekly scan quota is exhausted.
        /// </returns>
        [Authorize]
        [HttpGet("hits")]
        public async Task<ActionResult<IEnumerable<AllergenHitDto>>> GetHits([FromQuery] Guid recipeId, CancellationToken ct = default)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userId, out var userGuid))
                return Unauthorized();

            // Scan quota gate (shared unique index prevents double-counting with /alerts)
            var allowed = await _scanService.TryConsumeOrCountScanAsync(userGuid, recipeId, ct);
            if (!allowed)
            {
                var status = await _scanService.GetWeeklyScanStatusAsync(userGuid, ct);
                return StatusCode(402, new
                {
                    error = "scan_limit_reached",
                    scansUsed = status.ScansUsed,
                    scansLimit = status.ScansLimit,
                    weekResetsAt = status.WeekResetsAt
                });
            }

            // Get alerts from EF-based service
            var resp = await _alerts.GetAlertsForRecipeAsync(recipeId, userGuid);

            // Also pull quantities + global ids (so FE can match by IngredientId)
            // Ingredient.Name is NotMapped, so we join GlobalIngredients in the service; here we only need quantities.
            var ingMeta = await _db.Ingredients.AsNoTracking()
                .Where(i => i.RecipeId == recipeId)
                .Select(i => new { i.Id, i.Quantity, i.GlobalIngredientId })
                .ToDictionaryAsync(x => x.Id);

            var rows = new List<AllergenHitDto>();

            foreach (var alert in resp.Alerts)
            {
                foreach (var ing in alert.Ingredients)
                {
                    if (!ingMeta.TryGetValue(ing.IngredientId, out var meta))
                        continue;

                    rows.Add(new AllergenHitDto(
                        IngredientId: ing.IngredientId,
                        IngredientLabel: ing.IngredientName ?? "",
                        Quantity: meta.Quantity,
                        GlobalIngredientId: meta.GlobalIngredientId,
                        AllergenType: alert.AllergenType ?? "",
                        HitSource: ing.Source ?? "",
                        HitSeverity: ing.SeverityResolved ?? "low",
                        MayContain: ing.MayContain,
                        CrossContactRisk: ing.CrossContactRisk,
                        ProcessingAid: ing.ProcessingAid
                    ));
                }
            }

            return Ok(rows);
        }
    }
}
