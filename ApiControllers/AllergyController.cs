using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;
using System.Security.Claims;

namespace Platerly.Api.ApiControllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AllergyController : ControllerBase
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<AllergyController> _log;

        public AllergyController(PlaterlyDbContext db, ILogger<AllergyController> log)
        {
            _db = db;
            _log = log;
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
        // Returns enabled allergy names for the current user (lowercased for FE matching)
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
        // Replaces the user's enabled allergy set with the provided list of AllergyIds
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
                _db.UserAllergies.Add(new UserAllergy
                {
                    UserId = guid,
                    AllergyId = allergyId,
                    IsEnabled = true
                });
            }

            await _db.SaveChangesAsync();
            return Ok("Allergies updated.");
        }

        // NEW: GET /api/Allergy/hits?recipeId={GUID}
        // Wraps sp_GetRecipeAllergenHits_v2 and returns a flat DTO list.
        // If user not authenticated, returns [] (so anonymous browsing doesn't error).
        public record AllergenHitDto(
            Guid IngredientId,
            string? IngredientLabel,
            string? Quantity,
            Guid? GlobalIngredientId,
            string AllergenType,
            string HitSource,
            string? HitSeverity,
            int? HitSeverityLevel,
            bool MayContain,
            bool CrossContactRisk,
            bool ProcessingAid,
            bool LegacyIsAllergenicFlag,
            bool? GlobalIsAllergenicFlag
        );

        [HttpGet("hits")]
        public async Task<ActionResult<IEnumerable<AllergenHitDto>>> GetHits([FromQuery] Guid recipeId)
        {
            // Resolve current user (optional)
            Guid userGuid;
            var userClaim = User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userClaim, out userGuid))
            {
                // Not logged in → no personalized hits (frontend will show “No conflicts”)
                return Ok(Array.Empty<AllergenHitDto>());
            }

            var pUser = new SqlParameter("@UserId", userGuid);
            var pRecipe = new SqlParameter("@RecipeId", recipeId);

            // Use an ad-hoc DTO with HasNoKey (see DbContext config note below)
            var rows = await _db.Set<AllergenHitDto>()
                .FromSqlRaw("EXEC dbo.sp_GetRecipeAllergenHits_v2 @UserId, @RecipeId", pUser, pRecipe)
                .ToListAsync();

            return Ok(rows);
        }
    }
}
