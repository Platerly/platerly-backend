using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Platerly.Api.Models
{
    /// <summary>
    /// Types of admin actions that are audited
    /// </summary>
    public enum AdminAction
    {
        // Prohibited Ingredients
        CreateProhibitedIngredient,
        UpdateProhibitedIngredient,
        DeleteProhibitedIngredient,
        BulkCreateProhibitedIngredients,

        // Categories
        CreateCategory,
        UpdateCategory,
        DeleteCategory,

        // Global Ingredients
        UpdateGlobalIngredient,
        MergeIngredients,
        DeleteGlobalIngredient,

        // User Management
        PromoteUser,
        DemoteUser,
        SuspendUser,
        UnsuspendUser,

        // Substitutions
        VerifySubstitution,
        DeleteSubstitution,

        // Other
        ViewSensitiveData,
        ExportData,
        SystemConfigChange,
        SystemMigration
    }

    /// <summary>
    /// Audit log for tracking all admin actions.
    /// This provides accountability and security tracking.
    /// </summary>
    public class AdminAuditLog
    {
        [Key]
        public long Id { get; set; }

        /// <summary>
        /// The admin user who performed the action
        /// </summary>
        public Guid AdminUserId { get; set; }

        [ForeignKey(nameof(AdminUserId))]
        public User? AdminUser { get; set; }

        /// <summary>
        /// The type of action performed
        /// </summary>
        public AdminAction Action { get; set; }

        /// <summary>
        /// The type of entity affected (e.g., "ProhibitedIngredient", "User", "Category")
        /// </summary>
        [MaxLength(64)]
        public string EntityType { get; set; } = string.Empty;

        /// <summary>
        /// The ID of the affected entity (stored as string to support different ID types)
        /// </summary>
        [MaxLength(128)]
        public string? EntityId { get; set; }

        /// <summary>
        /// Human-readable description of the action
        /// </summary>
        [MaxLength(512)]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// JSON snapshot of the entity before the change (for updates/deletes)
        /// </summary>
        public string? OldValues { get; set; }

        /// <summary>
        /// JSON snapshot of the entity after the change (for creates/updates)
        /// </summary>
        public string? NewValues { get; set; }

        /// <summary>
        /// IP address of the admin at time of action
        /// </summary>
        [MaxLength(45)]
        public string? IpAddress { get; set; }

        /// <summary>
        /// User agent of the admin's browser/client
        /// </summary>
        [MaxLength(512)]
        public string? UserAgent { get; set; }

        /// <summary>
        /// Timestamp of the action (UTC)
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Whether the action was successful
        /// </summary>
        public bool WasSuccessful { get; set; } = true;

        /// <summary>
        /// Error message if the action failed
        /// </summary>
        [MaxLength(1024)]
        public string? ErrorMessage { get; set; }
    }
}
