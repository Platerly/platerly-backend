namespace Platerly.Api.Models
{
    public class PasswordResetToken
    {
        public int Id { get; set; }

        public Guid UserId { get; set; }  // ✅ Correct FK type
        public User User { get; set; }    // ✅ Navigation property

        public Guid Token { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; }
        public bool IsUsed { get; set; } = false;
    }
}
