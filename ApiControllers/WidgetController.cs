using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Safety;
using Platerly.Api.Services.Widget;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B White-Label Recipe Widget API — layer: B2B / public, base route: <c>api/Widget</c>.
    /// Enables third-party partners to embed a customisable Recipzee recipe feed on their own
    /// websites. Partners create a widget configuration (theme, filters, branding) once via
    /// the authenticated <c>configure</c> endpoint, then reference the resulting
    /// <c>configId</c> in the public <c>embed.js</c> script tag.
    /// Auth: API Key required for <c>configure</c>; <c>feed</c> and <c>public-config</c> are anonymous.
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
        /// <c>POST api/Widget/configure</c> — Create or update a widget configuration.
        /// Persists theme settings (colours, font, layout), recipe filters (dietary tags,
        /// allergen exclusions, region), and branding assets, then returns a <c>configId</c>
        /// and a ready-to-use embed URL for the partner's site.
        /// </summary>
        /// <param name="request">Widget configuration payload including theme and filter options.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the configuration result
        /// with <c>configId</c> and <c>embedUrl</c>.<br/>
        /// <c>401 Unauthorized</c> — missing, invalid, or unresolvable API key.
        /// </returns>
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
        /// <c>GET api/Widget/feed?configId={guid}</c> — Retrieve the recipe feed for an embedded widget.
        /// Called by the <c>embed.js</c> script at render time. Returns the filtered recipe list
        /// based on the stored configuration for the given <paramref name="configId"/>.
        /// No authentication is required — the config ID acts as a capability token.
        /// </summary>
        /// <param name="configId">GUID of the widget configuration to render.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the recipe feed.<br/>
        /// <c>400 Bad Request</c> — <paramref name="configId"/> is empty.
        /// </returns>
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
        /// <c>GET api/Widget/public-config/{configId}</c> — Fetch branding settings for the embed script.
        /// Called by <c>embed.js</c> on page load to apply partner-specific colours, logo URL,
        /// and placeholder text before the feed is rendered. Response is cached for 5 minutes
        /// (<c>Cache-Control: public, max-age=300</c>) and served with an open CORS header so it
        /// can be fetched from any origin.
        /// No authentication is required.
        /// </summary>
        /// <param name="configId">GUID of the widget configuration whose branding to return.</param>
        /// <returns>
        /// <c>200 OK</c> — public branding config JSON (not wrapped in safety envelope).<br/>
        /// <c>404 Not Found</c> — configuration does not exist or has been deactivated.
        /// </returns>
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
