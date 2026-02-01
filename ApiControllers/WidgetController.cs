using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Widget;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B White-Label Recipe Widget API.
    /// Configure and serve recipe feeds for embedding in third-party sites.
    /// Requires API Key authentication (except feed endpoint).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class WidgetController : ControllerBase
    {
        private readonly IWidgetService _widget;
        private readonly ILogger<WidgetController> _logger;

        public WidgetController(
            IWidgetService widget,
            ILogger<WidgetController> logger)
        {
            _widget = widget;
            _logger = logger;
        }

        /// <summary>
        /// Create or update widget configuration.
        /// Set theme, filters, and get an embed URL.
        /// </summary>
        [HttpPost("configure")]
        [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
        public async Task<ActionResult<WidgetConfigResponse>> Configure(
            [FromBody] WidgetConfigRequest request,
            CancellationToken ct = default)
        {
            var apiKeyId = GetApiKeyId();
            if (apiKeyId == Guid.Empty) return Unauthorized();

            var result = await _widget.ConfigureWidgetAsync(apiKeyId, request, ct);
            return Ok(result);
        }

        /// <summary>
        /// Get widget recipe feed (public endpoint).
        /// This endpoint is called by the embedded widget and does not require API key auth in headers.
        /// </summary>
        /// <param name="configId">The widget configuration ID</param>
        [HttpGet("feed")]
        [AllowAnonymous]
        public async Task<ActionResult<WidgetFeedResponse>> GetFeed(
            [FromQuery] Guid configId,
            CancellationToken ct = default)
        {
            if (configId == Guid.Empty)
            {
                return BadRequest("Widget config ID is required");
            }

            var result = await _widget.GetWidgetFeedAsync(configId, ct);
            return Ok(result);
        }

        private Guid GetApiKeyId()
        {
            var claim = User.FindFirst("ApiKeyId");
            return claim != null && Guid.TryParse(claim.Value, out var id) ? id : Guid.Empty;
        }
    }
}
