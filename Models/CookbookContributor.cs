namespace Platerly.Api.Models
{
    public class CookbookContributor
    {
        public Guid CookbookId { get; set; }
        public Cookbook Cookbook { get; set; } = null!;

        public Guid UserId { get; set; }
        public User User { get; set; } = null!;

        // Keep it simple: either "Write" (can modify recipes) or "Read"
        public string Role { get; set; } = "Write";
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    }
}
