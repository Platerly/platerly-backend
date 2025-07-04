using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
        private readonly PlaterlyDbContext _context;

        public AllergyController(PlaterlyDbContext context)
        {
            _context = context;
        }

        // GET: /api/Allergy/all
        [HttpGet("all")]
        public async Task<IActionResult> GetAllAllergies()
        {
            var allergies = await _context.Allergies
                .OrderBy(a => a.Name)
                .ToListAsync();

            return Ok(allergies);
        }

        // ✅ FIXED: GET /api/Allergy/my-allergies
        // Returns allergy names (e.g., "milk", "eggs") for matching
        [Authorize]
        [HttpGet("my-allergies")]
        public async Task<IActionResult> GetMyAllergies()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var guid = Guid.Parse(userId);

            var allergyNames = await _context.UserAllergies
                .Where(ua => ua.UserId == guid)
                .Include(ua => ua.Allergy)
                .Select(ua => ua.Allergy.Name.ToLower()) // 👈 Match frontend expectation
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

            // Remove existing
            var existing = await _context.UserAllergies
                .Where(ua => ua.UserId == guid)
                .ToListAsync();

            _context.UserAllergies.RemoveRange(existing);

            // Add new
            foreach (var allergyId in newAllergyIds.Distinct())
            {
                _context.UserAllergies.Add(new UserAllergy
                {
                    UserId = guid,
                    AllergyId = allergyId
                });
            }

            await _context.SaveChangesAsync();
            return Ok("Allergies updated.");
        }
    }
}
