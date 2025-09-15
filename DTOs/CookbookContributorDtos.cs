using System;

namespace Platerly.Api.DTOs
{
    // Owner sends this to add a contributor
    public class AddContributorDto
    {
        public Guid UserId { get; set; }
        // Keep it simple for now: "Write" lets them modify recipes. (You can add "Read" later.)
        public string Role { get; set; } = "Write";
        public string? Username { get; set; }
    }

    // Returned when listing contributors
    public class ContributorDto
    {
        public Guid UserId { get; set; }
        public string Role { get; set; } = "Write";
        public string? DisplayName { get; set; }   // optional convenience field
        public DateTime AddedAt { get; set; }
    }

    // (Optional) For promoting/demoting later
    public class UpdateContributorRoleDto
    {
        public string Role { get; set; } = "Write";
    }

    //Used by /api/Cookbook/can-write
    public class CookbookWriteSummaryDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public bool IsOwner { get; set; }
    }
}
