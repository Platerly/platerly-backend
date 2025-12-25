using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Services.Allergy;
using System.Security.Claims;

namespace Platerly.Api.ApiControllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AllergyController : ControllerBase
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<AllergyController> _log;
        private readonly IAllergyAlertService _alerts;

        public AllergyController(
            PlaterlyDbContext db,
            ILogger<AllergyController> log,
            IAllergyAlertService alerts)
        {
            _db = db;
            _log = log;
            _alerts = alerts;
        }

        // GET: /api/Allergy/all
        [HttpGet("all")]
        public async Task<IActionResult> GetAllAllergies()
        {
            var allergies = await _db.Allergies
                .OrderBy(a => a.Name)
                .ToListAsync();

            return Ok(allergies);
        }

        // GET: /api/Allergy/my-allergies
        [Authorize]
        [HttpGet("my-allergies")]
        public async Task<IActionResult> GetMyAllergies()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var guid = Guid.Parse(userId);

            var allergyNames = await _db.UserAllergies
                .Where(ua => ua.UserId == guid && ua.IsEnabled)
                .Include(ua => ua.Allergy)
                .OrderBy(ua => ua.Allergy!.Name)
                .Select(ua => ua.Allergy!.Name.ToLower())
                .ToListAsync();

            return Ok(allergyNames);
        }

        // PUT: /api/Allergy/update
        [Authorize]
        [HttpPut("update")]
        public async Task<IActionResult> UpdateUserAllergies([FromBody] List<Guid> newAllergyIds)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var guid = Guid.Parse(userId);

            var existing = await _db.UserAllergies
                .Where(ua => ua.UserId == guid)
                .ToListAsync();

            _db.UserAllergies.RemoveRange(existing);

            foreach (var allergyId in newAllergyIds.Distinct())
            {
                _db.UserAllergies.Add(new Models.UserAllergy
                {
                    UserId = guid,
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

        [Authorize]
        [HttpGet("hits")]
        public async Task<ActionResult<IEnumerable<AllergenHitDto>>> GetHits([FromQuery] Guid recipeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userId, out var userGuid))
                return Unauthorized();

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
