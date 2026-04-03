using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using System.Linq;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// Public B2C controller for recipe tag autocomplete.
    /// Returns tag names and their usage counts so the frontend can power
    /// the tag picker on the recipe editor and the filter panel on the feed.
    /// Base route: <c>api/tags</c>.
    /// Anonymous access; rate-limited via the <c>anonymous</c> policy.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class TagsController : ControllerBase
    {
        private readonly PlaterlyDbContext _context;

        public TagsController(PlaterlyDbContext context) => _context = context;

        /// <summary>
        /// Return tags whose name contains the given search string (case-insensitive substring
        /// match). When <paramref name="search"/> is omitted or null, returns up to
        /// <paramref name="take"/> tags in alphabetical order. Each result includes the tag's
        /// current recipe usage count.
        /// </summary>
        /// <param name="search">Optional substring to filter tag names (max 50 characters).</param>
        /// <param name="take">Maximum results to return, clamped 1–50 (default 20).</param>
        /// <returns>
        /// <c>200 OK</c> — array of <c>{ id: int, name: string, usageCount: int }</c>.<br/>
        /// <c>400</c> — search string exceeds 50 characters.<br/>
        /// <c>429</c> — rate limit exceeded.
        /// </returns>
        // GET /api/tags?search=ve&take=20
        [AllowAnonymous]
        [HttpGet]
        [EnableRateLimiting("anonymous")]
        public async Task<IActionResult> Suggest([FromQuery] string? search = null, [FromQuery] int take = 20)
        {
            if (search != null && search.Length > 50)
                return BadRequest("Search query too long (max 50 characters)");

            take = Math.Clamp(take, 1, 50);

            var q = _context.Tags.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                q = q.Where(t => t.Name.ToLower().Contains(s));
            }

            // Optional: include usage count
            var results = await q
                .OrderBy(t => t.Name)
                .Select(t => new
                {
                    t.Id,
                    name = t.Name,
                    usageCount = _context.RecipeTags.Count(rt => rt.TagId == t.Id)
                })
                .Take(take)
                .ToListAsync();

            return Ok(results);
        }
    }
}
