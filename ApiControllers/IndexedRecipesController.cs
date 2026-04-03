using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using System.Security.Claims;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2C Indexed Recipe Save Count API — layer: B2C, base route: <c>api/indexed-recipes</c>.
    /// Exposes read-only save-count data for the authenticated user's indexed (saved) recipes
    /// across all their cookbooks. The actual save and remove operations live in
    /// <c>CookbookController</c> at <c>POST/DELETE api/Cookbook/{id}/indexed-recipes</c>.
    /// This controller exists solely to serve the lightweight count query used by the frontend
    /// to enforce the free-tier save limit of 3 recipes.
    /// Auth: JWT bearer (<c>[Authorize]</c>).
    /// </summary>
    [ApiController]
    [Route("api/indexed-recipes")]
    [Authorize]
    public class IndexedRecipesController : ControllerBase
    {
        private readonly PlaterlyDbContext _db;

        public IndexedRecipesController(PlaterlyDbContext db) => _db = db;

        /// <summary>
        /// Resolves the authenticated user's ID from JWT claims.
        /// Checks <see cref="ClaimTypes.NameIdentifier"/> first, then the <c>"sub"</c> claim
        /// as a fallback for tokens issued by external providers (e.g., Google OAuth).
        /// Returns <c>null</c> if neither claim is present or cannot be parsed as a GUID.
        /// </summary>
        private Guid? CurrentUserId()
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? User.FindFirstValue("sub");
            return Guid.TryParse(raw, out var g) ? g : null;
        }

        /// <summary>
        /// <c>GET api/indexed-recipes/saved/count</c> — Get the number of indexed recipes the current user has saved.
        /// Counts <c>CookbookIndexedRecipes</c> rows belonging to the authenticated user across
        /// all their cookbooks. The frontend uses this value to enforce the free-tier limit of 3
        /// saved recipes and to show the save counter in the UI.
        /// </summary>
        /// <returns>
        /// <c>200 OK</c> — <c>{ count: int }</c> with the total number of saved indexed recipes.<br/>
        /// <c>401 Unauthorized</c> — JWT is missing, expired, or the user ID cannot be resolved.
        /// </returns>
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
