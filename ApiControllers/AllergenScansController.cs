using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.Services.Allergy;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2C Allergen Scan Quota controller. Exposes the current user's weekly scan quota status.
    ///
    /// Base route: /api/allergen-scans
    /// Auth requirement: [Authorize] (JWT).
    ///
    /// Scan quota system:
    ///   • Free users are limited to 3 unique recipe scans per rolling calendar week
    ///     (week start = Monday 00:00 UTC, as computed by <c>AllergenScanService.GetWeekStart</c>).
    ///   • A "scan" is consumed when <c>GET /api/Allergy/hits</c> or <c>GET /api/Recipe/{id}/alerts</c>
    ///     is called for a recipe the user has not scanned in the current week.
    ///   • The uniqueness constraint <c>(UserId, RecipeId, WeekStartDate)</c> on
    ///     <c>UserAllergenScan</c> prevents double-counting the same recipe in the same week.
    ///   • Premium and trial users bypass the quota check entirely — no <c>UserAllergenScan</c>
    ///     row is written for them.
    ///   • Quota gates on hitting the limit return HTTP 402 with scan status payload
    ///     so the frontend can render the appropriate upgrade prompt or blur overlay.
    /// </summary>
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
        ///
        /// Response fields:
        ///   <c>scansUsed</c>    — number of unique recipes scanned this week.
        ///   <c>scansLimit</c>   — weekly cap (3 for free users; null for premium/trial).
        ///   <c>scansRemaining</c> — scans left this week (null for premium/trial).
        ///   <c>weekResetsAt</c> — UTC timestamp of next Monday midnight when the quota resets.
        ///   <c>isPremium</c>    — true when user is premium or on active trial.
        ///
        /// The frontend <c>useScanStatus.js</c> hook caches this response for 2 minutes
        /// and exposes an <c>atLimit</c> derived field. Call <c>invalidate()</c> / call
        /// <c>clearScanStatusCache()</c> after a scan succeeds or a 402 is received.
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
