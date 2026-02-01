namespace Platerly.Api.DTOs
{
    public class WebhookSubscribeRequest
    {
        public string Url { get; set; } = string.Empty;
        public List<string> EventTypes { get; set; } = new();
    }

    public class WebhookSubscriptionDto
    {
        public Guid Id { get; set; }
        public string Url { get; set; } = string.Empty;
        public List<string> EventTypes { get; set; } = new();
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastTriggeredAt { get; set; }
        public int ConsecutiveFailures { get; set; }
    }

    public class WebhookListResponse
    {
        public List<WebhookSubscriptionDto> Subscriptions { get; set; } = new();
    }

    public class WebhookTestResponse
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public int StatusCode { get; set; }
    }
}
