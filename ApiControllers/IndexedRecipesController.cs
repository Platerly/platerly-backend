using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using System.Security.Claims;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// Endpoints for user-facing indexed recipe saves.
    /// Save/remove lives in CookbookController at /api/Cookbook/{id}/indexed-recipes.
    /// This controller exposes the save count for the free-tier gate check.
    /// </summary>
    [ApiController]
    [Route("api/indexed-recipes")]
    [Authorize]
    public class IndexedRecipesController : ControllerBase
    {
        private readonly PlaterlyDbContext _db;

        public IndexedRecipesController(PlaterlyDbContext db) => _db = db;

        private Guid? CurrentUserId()
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? User.FindFirstValue("sub");
            return Guid.TryParse(raw, out var g) ? g : null;
        }

        /// <summary>
        /// GET /api/indexed-recipes/saved/count
        /// Returns how many indexed recipes the current user has saved (across all cookbooks).
        /// Used by the frontend to enforce the free-tier save limit of 3.
        /// </summary>
        [HttpGet("saved/count")]
        public async Task<IActionResult> GetSavedCount(CancellationToken ct = default)
        {
            var uid = CurrentUserId();
            if (uid is null) return Unauthorized();

            var count = await _db.CookbookIndexedRecipes
                .CountAsync(ci => ci.SavedByUserId == uid.Value, ct);

            return Ok(new { count });
        }
    }
}
