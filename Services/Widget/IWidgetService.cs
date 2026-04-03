using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Widget
{
    /// <summary>Manages B2B embeddable widget configuration and provides the public feed and branding config for <c>embed.js</c>.</summary>
    public interface IWidgetService
    {
        Task<WidgetConfigResponse> ConfigureWidgetAsync(
            Guid apiKeyId, WidgetConfigRequest request,
            CancellationToken ct = default);

        Task<WidgetFeedResponse> GetWidgetFeedAsync(
            Guid widgetConfigId,
            CancellationToken ct = default);

        /// <summary>
        /// Returns public branding config for embed.js — no auth required.
        /// Returns null when the config does not exist or is inactive.
        /// </summary>
        Task<WidgetPublicConfigDto?> GetPublicConfigAsync(
            Guid configId,
            CancellationToken ct = default);
    }
}
