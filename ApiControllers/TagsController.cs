using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using System.Linq;

namespace Platerly.Api.ApiControllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TagsController : ControllerBase
    {
        private readonly PlaterlyDbContext _context;

        public TagsController(PlaterlyDbContext context) => _context = context;

        /// <summary>
        /// Autocomplete tags by prefix/substring.
        /// GET /api/tags?search=ve&take=20
        /// </summary>
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
