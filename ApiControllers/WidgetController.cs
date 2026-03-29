using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Safety;
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
    public class WidgetController : B2BControllerBase
    {
        private readonly IWidgetService _widget;
        private readonly ILogger<WidgetController> _logger;

        public WidgetController(
            IWidgetService widget,
            ILogger<WidgetController> logger,
            ISafetyMetaService safetyMeta)
            : base(safetyMeta)
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
        public async Task<ActionResult> Configure(
            [FromBody] WidgetConfigRequest request,
            CancellationToken ct = default)
        {
            var apiKeyId = GetApiKeyId();
            if (apiKeyId == Guid.Empty) return ApiError("API key identity could not be resolved.", 401);

            var result = await _widget.ConfigureWidgetAsync(apiKeyId, request, ct);
            return SafetyOk(result);
        }

        /// <summary>
        /// Get widget recipe feed (public endpoint).
        /// This endpoint is called by the embedded widget and does not require API key auth in headers.
        /// </summary>
        [HttpGet("feed")]
        [AllowAnonymous]
        public async Task<ActionResult> GetFeed(
            [FromQuery] Guid configId,
            CancellationToken ct = default)
        {
            if (configId == Guid.Empty)
                return ApiError("Widget config ID is required.");

            var result = await _widget.GetWidgetFeedAsync(configId, ct);
            return SafetyOk(result);
        }

        /// <summary>
        /// Get public branding config for embed.js.
        /// Called by the embed script on page load to apply client colours, logo, and placeholder text.
        /// No authentication required.
        /// </summary>
        [HttpGet("public-config/{configId:guid}")]
        [AllowAnonymous]
        public async Task<ActionResult> GetPublicConfig(
            Guid configId,
            CancellationToken ct = default)
        {
            var result = await _widget.GetPublicConfigAsync(configId, ct);
            if (result == null)
                return NotFound(new { error = "Widget configuration not found or inactive." });

            // Allow cross-origin access so embed.js works on third-party sites
            Response.Headers.Append("Access-Control-Allow-Origin", "*");
            Response.Headers.Append("Cache-Control", "public, max-age=300");

            return Ok(result);
        }
    }
}
