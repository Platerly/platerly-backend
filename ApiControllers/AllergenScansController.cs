using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.Services.Allergy;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Platerly.Api.ApiControllers
{
    [ApiController]
    [Route("api/allergen-scans")]
    [Authorize]
    public class AllergenScansController : ControllerBase
    {
        private readonly IAllergenScanService _scanService;

        public AllergenScansController(IAllergenScanService scanService)
        {
            _scanService = scanService;
        }

        /// <summary>
        /// Returns the authenticated user's weekly allergen scan quota status.
        /// Premium users get null limits (unlimited).
        /// </summary>
        [HttpGet("status")]
        public async Task<IActionResult> GetStatus(CancellationToken ct)
        {
            var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                         ?? User.FindFirst("sub")?.Value;

            if (!Guid.TryParse(userIdStr, out var userId))
                return Unauthorized();

            var status = await _scanService.GetWeeklyScanStatusAsync(userId, ct);

            return Ok(new
            {
                scansUsed = status.ScansUsed,
                scansLimit = status.ScansLimit,
                scansRemaining = status.ScansRemaining,
                weekResetsAt = status.WeekResetsAt,
                isPremium = status.IsPremium
            });
        }
    }
}
