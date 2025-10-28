using Microsoft.AspNetCore.Mvc;

namespace Platerly.Api.ApiControllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        [HttpGet] public IActionResult Get() => Ok(new { ok = true, time = DateTime.UtcNow });
    }

}
