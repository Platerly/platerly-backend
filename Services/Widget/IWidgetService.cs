using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Widget
{
    public interface IWidgetService
    {
        Task<WidgetConfigResponse> ConfigureWidgetAsync(
            Guid apiKeyId, WidgetConfigRequest request,
            CancellationToken ct = default);

        Task<WidgetFeedResponse> GetWidgetFeedAsync(
            Guid widgetConfigId,
            CancellationToken ct = default);
    }
}
