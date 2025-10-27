namespace Platerly.Api.Contracts.Billing
{
    public record CreateCheckoutRequest(string UserId);
    public record CreatePortalRequest(string UserId);
    public record CancelRequest(string UserId);     // cancel at period end
    public record ResumeRequest(string UserId);     // undo cancel_at_period_end
}
