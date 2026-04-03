using Microsoft.AspNetCore.Mvc;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// Infrastructure health-check endpoint — layer: public, base route: <c>api/Health</c>.
    /// Returns a lightweight JSON response confirming the API process is alive and includes
    /// the current UTC timestamp. Intended for use by Azure health probes, uptime monitors,
    /// and load-balancer liveness checks. No authentication is required.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        /// <summary>
        /// <c>GET api/Health</c> — Liveness check.
        /// Returns <c>{ ok: true, time: &lt;UTC ISO-8601&gt; }</c> when the API process is running.
        /// A non-200 response (or network timeout) indicates the service is unhealthy.
        /// </summary>
        /// <returns><c>200 OK</c> — <c>{ ok: true, time: string }</c>.</returns>
        [HttpGet] public IActionResult Get() => Ok(new { ok = true, time = DateTime.UtcNow });
    }

}
