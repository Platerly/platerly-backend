using Microsoft.EntityFrameworkCore;
using Platerly.Api.Models;

namespace Platerly.Api.Data
{
    public class PlaterlyDbContext : DbContext
    {
        public PlaterlyDbContext(DbContextOptions<PlaterlyDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Recipe> Recipes { get; set; }
        public DbSet<RecipeLike> RecipeLikes { get; set; }
        public DbSet<Ingredient> Ingredients { get; set; }
        public DbSet<UserFollow> UserFollows { get; set; }
        public DbSet<Comment> Comments { get; set; }
        public DbSet<RecipeRepost> RecipeReposts { get; set; }
        public DbSet<RecipeRemix> RecipeRemixes { get; set; }
        public DbSet<Cookbook> Cookbooks { get; set; } = null!;
        public DbSet<CookbookRecipe> CookbookRecipes { get; set; } = null!;
        public DbSet<Allergy> Allergies { get; set; }
        public DbSet<UserAllergy> UserAllergies { get; set; }
        public DbSet<VerificationToken> VerificationTokens { get; set; }
        public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }
        public DbSet<GlobalIngredient> GlobalIngredients { get; set; }
        public DbSet<IngredientAllergen> IngredientAllergens { get; set; }
        public DbSet<GlobalIngredientAllergen> GlobalIngredientAllergens { get; set; }
        public DbSet<CookbookContributionRequest> CookbookContributionRequests { get; set; }
        public DbSet<CookbookContributor> CookbookContributors { get; set; }
        public DbSet<Tag> Tags { get; set; } = null!;
        public DbSet<RecipeTag> RecipeTags { get; set; } = null!;
        public DbSet<RecipeTrendingCache> RecipeTrendingCache { get; set; } = null!;
        public DbSet<RecipeView> RecipeViews { get; set; } = null!;
        public DbSet<RecipeTrendingHistory> RecipeTrendingHistory { get; set; } = null!;
        public DbSet<UserTrendingCache> UserTrendingCache { get; set; } = null!;
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<AllergenKeyword> AllergenKeywords { get; set; } = null!;
        public DbSet<AllergenKeywordAllergy> AllergenKeywordAllergies { get; set; } = null!;
        public DbSet<BillingEmailEvent> BillingEmailEvents { get; set; }
        public DbSet<Notification> Notifications { get; set; } = null!;
        public DbSet<CommentLike> CommentLikes { get; set; } = null!;

        // ---- B2B Analytics & Safety ----
        public DbSet<ProhibitedIngredient> ProhibitedIngredients { get; set; } = null!;
        public DbSet<IngredientCategory> IngredientCategories { get; set; } = null!;
        public DbSet<IngredientSubstitution> IngredientSubstitutions { get; set; } = null!;
        public DbSet<SubstitutionVote> SubstitutionVotes { get; set; } = null!;
        public DbSet<IngredientUsageLog> IngredientUsageLogs { get; set; } = null!;
        public DbSet<IngredientTrendDaily> IngredientTrendDaily { get; set; } = null!;
        public DbSet<RegionalIngredientStats> RegionalIngredientStats { get; set; } = null!;
        public DbSet<IngredientPairing> IngredientPairings { get; set; } = null!;
        public DbSet<DietaryTrend> DietaryTrends { get; set; } = null!;
        public DbSet<IngredientSearchLog> IngredientSearchLogs { get; set; } = null!;

        // ---- Admin & Security ----
        public DbSet<AdminAuditLog> AdminAuditLogs { get; set; } = null!;

        // ---- Shopping Lists & Meal Planning ----
        public DbSet<ShoppingList> ShoppingLists { get; set; } = null!;
        public DbSet<ShoppingListItem> ShoppingListItems { get; set; } = null!;
        public DbSet<ShoppingListItemSource> ShoppingListItemSources { get; set; } = null!;
        public DbSet<ShoppingListRecipe> ShoppingListRecipes { get; set; } = null!;
        public DbSet<MealPlan> MealPlans { get; set; } = null!;
        public DbSet<MealPlanEntry> MealPlanEntries { get; set; } = null!;

        // ---- API Keys ----
        public DbSet<ApiKey> ApiKeys { get; set; } = null!;
        public DbSet<ApiKeyUsageLog> ApiKeyUsageLogs { get; set; } = null!;

        // ---- B2B Nutrition ----
        public DbSet<NutritionCache> NutritionCaches { get; set; } = null!;

        // ---- B2B Webhooks ----
        public DbSet<WebhookSubscription> WebhookSubscriptions { get; set; } = null!;
        public DbSet<WebhookEvent> WebhookEvents { get; set; } = null!;

        // ---- B2B Widget ----
        public DbSet<WidgetConfig> WidgetConfigs { get; set; } = null!;

        // ---- B2B Sustainability ----
        public DbSet<IngredientSustainabilityData> IngredientSustainabilityData { get; set; } = null!;

        // ---- Allergen Scan Quota ----
        public DbSet<UserAllergenScan> UserAllergenScans { get; set; } = null!;


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ---------- Global Filters ----------
            modelBuilder.Entity<Recipe>().HasQueryFilter(r => !r.IsDeleted);

            // ---------- Users ----------
            modelBuilder.Entity<User>(b =>
            {
                b.HasIndex(u => u.Username).IsUnique();
                b.HasIndex(u => u.Email).IsUnique();

                b.Property(u => u.Username).HasMaxLength(64).IsRequired();
                b.Property(u => u.Email).HasMaxLength(256).IsRequired();
                b.Property(u => u.DisplayName).HasMaxLength(128);
                b.Property(u => u.Bio).HasMaxLength(1024);

                b.Property(u => u.ProfileImageUrl).HasMaxLength(1024);
                b.Property(u => u.ProfileImageBlobName).HasMaxLength(256);

                b.Property(u => u.RowVersion).IsRowVersion();
                b.Property(u => u.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            });

            // ---------- Recipes ----------
            modelBuilder.Entity<Recipe>(b =>
            {
                b.HasIndex(r => r.UserId);
                b.HasIndex(r => r.CreatedAt);
                b.HasIndex(r => r.IsDeleted);

                b.Property(r => r.Title).HasMaxLength(160).IsRequired();
                b.Property(r => r.Description).HasMaxLength(4000);
                b.Property(r => r.Instructions).HasMaxLength(8000);

                b.Property(r => r.Region).HasMaxLength(64);
                b.Property(r => r.ImageUrl).HasMaxLength(1024);
                b.Property(r => r.ImageBlobName).HasMaxLength(256);

                b.Property(r => r.Difficulty).HasConversion<string>().HasMaxLength(16).IsUnicode(false).IsRequired(false);
                b.Property(r => r.IsDeleted).HasDefaultValue(false);
                b.Property(r => r.UpdatedAt);
                b.Property(r => r.DeletedAt);
                b.Property(r => r.DeletedByUserId);

                b.Property(r => r.RowVersion).IsRowVersion();
                b.Property(r => r.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            });

            //---------- Tags ----------
            modelBuilder.Entity<Tag>(b =>
            {
                b.HasIndex(t => t.Name).IsUnique();
                b.Property(t => t.Name).HasMaxLength(64).IsRequired();

                b.HasData(
                    new Tag { Id = 1, Name = "Healthy" },
                    new Tag { Id = 2, Name = "Comfort Food" },
                    new Tag { Id = 3, Name = "Unhealthy" },
                    new Tag { Id = 4, Name = "Treat" }
                );
            });

            // ---------- RecipeTag ----------
            modelBuilder.Entity<RecipeTag>(b =>
            {
                b.HasKey(rt => new { rt.RecipeId, rt.TagId });

                b.HasOne(rt => rt.Recipe)
                    .WithMany(r => r.RecipeTags)
                    .HasForeignKey(rt => rt.RecipeId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(rt => rt.Tag)
                    .WithMany(t => t.RecipeTags)
                    .HasForeignKey(rt => rt.TagId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // ---------- Follow system ----------
            modelBuilder.Entity<UserFollow>(b =>
            {
                b.HasKey(uf => new { uf.FollowerId, uf.FollowingId });

                // Index for "get all followers of user X" queries
                b.HasIndex(uf => uf.FollowingId);
                // Index for "get all users that user X follows" queries
                b.HasIndex(uf => uf.FollowerId);

                b.HasOne(uf => uf.Follower)
                    .WithMany(u => u.Following)
                    .HasForeignKey(uf => uf.FollowerId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(uf => uf.Following)
                    .WithMany(u => u.Followers)
                    .HasForeignKey(uf => uf.FollowingId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // ---------- Likes ----------
            modelBuilder.Entity<RecipeLike>(b =>
            {
                b.HasKey(rl => new { rl.UserId, rl.RecipeId });

                // Index for "get all likes on recipe X" and "count likes" queries
                b.HasIndex(rl => rl.RecipeId);
                // Index for "get all recipes user X liked" queries
                b.HasIndex(rl => rl.UserId);

                b.HasOne(rl => rl.User)
                    .WithMany()
                    .HasForeignKey(rl => rl.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(rl => rl.Recipe)
                    .WithMany(r => r.Likes)
                    .HasForeignKey(rl => rl.RecipeId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // ---------- Comments ----------
            modelBuilder.Entity<Comment>(b =>
            {
                // Index for "get all comments on recipe X" queries (most common)
                b.HasIndex(c => c.RecipeId);
                // Index for "get all comments by user X" queries
                b.HasIndex(c => c.UserId);
                // Composite index for paginated comments on a recipe, ordered by date
                b.HasIndex(c => new { c.RecipeId, c.CreatedAt });
                // Index for replies
                b.HasIndex(c => c.ParentCommentId);

                b.Property(c => c.Content).HasMaxLength(2000).IsRequired();

                b.HasOne(c => c.User)
                    .WithMany()
                    .HasForeignKey(c => c.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(c => c.Recipe)
                    .WithMany()
                    .HasForeignKey(c => c.RecipeId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Self-referencing for replies
                b.HasOne(c => c.ParentComment)
                    .WithMany(c => c.Replies)
                    .HasForeignKey(c => c.ParentCommentId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // ---------- Comment Likes ----------
            modelBuilder.Entity<CommentLike>(b =>
            {
                b.HasKey(cl => new { cl.UserId, cl.CommentId });

                b.HasIndex(cl => cl.CommentId);
                b.HasIndex(cl => cl.UserId);

                b.HasOne(cl => cl.User)
                    .WithMany()
                    .HasForeignKey(cl => cl.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(cl => cl.Comment)
                    .WithMany(c => c.Likes)
                    .HasForeignKey(cl => cl.CommentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ---------- Notifications ----------
            modelBuilder.Entity<Notification>(b =>
            {
                b.HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAt });
                b.HasIndex(n => n.UserId);
                b.HasIndex(n => n.ActorId);
                b.HasIndex(n => n.CreatedAt);

                b.Property(n => n.Type)
                    .HasConversion<string>()
                    .HasMaxLength(16)
                    .IsUnicode(false);

                b.Property(n => n.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

                b.HasOne(n => n.User)
                    .WithMany()
                    .HasForeignKey(n => n.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(n => n.Actor)
                    .WithMany()
                    .HasForeignKey(n => n.ActorId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(n => n.Recipe)
                    .WithMany()
                    .HasForeignKey(n => n.RecipeId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(n => n.Comment)
                    .WithMany()
                    .HasForeignKey(n => n.CommentId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // ---------- Reposts ----------
            modelBuilder.Entity<RecipeRepost>().HasKey(rr => new { rr.UserId, rr.RecipeId });

            modelBuilder.Entity<RecipeRepost>()
                .HasOne(rr => rr.User)
                .WithMany()
                .HasForeignKey(rr => rr.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<RecipeRepost>()
                .HasOne(rr => rr.Recipe)
                .WithMany(r => r.Reposts)
                .HasForeignKey(rr => rr.RecipeId)
                .OnDelete(DeleteBehavior.Cascade);

            // ---------- Remix system ----------
            modelBuilder.Entity<RecipeRemix>().HasKey(rr => rr.Id);

            modelBuilder.Entity<RecipeRemix>()
                .HasOne(rr => rr.RemixRecipe)
                .WithOne()
                .HasForeignKey<RecipeRemix>(rr => rr.RemixRecipeId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<RecipeRemix>()
                .HasOne(rr => rr.OriginalRecipe)
                .WithMany()
                .HasForeignKey(rr => rr.OriginalRecipeId)
                .OnDelete(DeleteBehavior.Restrict);

            // ---------- Cookbooks ----------
            modelBuilder.Entity<Cookbook>(b =>
            {
                b.HasIndex(cb => cb.UserId);
                b.HasIndex(cb => cb.IsPublic);

                b.Property(cb => cb.Title).HasMaxLength(160).IsRequired();
                b.Property(cb => cb.Description).HasMaxLength(1000);
                b.Property(cb => cb.CoverImageUrl).HasMaxLength(1024);
                b.Property(cb => cb.CoverImageBlobName).HasMaxLength(256);
                b.Property(cb => cb.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
                b.Property(cb => cb.RowVersion).IsRowVersion();

                b.HasOne(cb => cb.User)
                    .WithMany(u => u.Cookbooks)
                    .HasForeignKey(cb => cb.UserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // ---------- CookbookRecipe ----------
            modelBuilder.Entity<CookbookRecipe>().HasKey(cr => new { cr.CookbookId, cr.RecipeId });

            modelBuilder.Entity<CookbookRecipe>()
                .Property(cr => cr.Note)
                .HasMaxLength(512);

            modelBuilder.Entity<CookbookRecipe>()
                .HasIndex(cr => new { cr.CookbookId, cr.SortOrder });

            modelBuilder.Entity<CookbookRecipe>()
                .HasIndex(cr => cr.AddedByUserId);

            modelBuilder.Entity<CookbookRecipe>()
                .HasOne(cr => cr.Cookbook)
                .WithMany(c => c.Recipes)
                .HasForeignKey(cr => cr.CookbookId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CookbookRecipe>()
                .HasOne(cr => cr.Recipe)
                .WithMany()
                .HasForeignKey(cr => cr.RecipeId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CookbookRecipe>()
                .HasOne(cr => cr.AddedByUser)
                .WithMany()
                .HasForeignKey(cr => cr.AddedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CookbookRecipe>()
                .Property(cr => cr.AddedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()");

            // ---------- Cookbook Contributors ----------
            modelBuilder.Entity<CookbookContributor>().HasKey(cc => new { cc.CookbookId, cc.UserId });

            modelBuilder.Entity<CookbookContributor>()
                .HasOne(cc => cc.Cookbook)
                .WithMany(cb => cb.Contributors)
                .HasForeignKey(cc => cc.CookbookId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CookbookContributor>()
                .HasOne(cc => cc.User)
                .WithMany()
                .HasForeignKey(cc => cc.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CookbookContributor>()
                .Property(cc => cc.Role).HasMaxLength(16).IsRequired();

            // ---------- Cookbook Contribution Requests ----------
            modelBuilder.Entity<CookbookContributionRequest>(b =>
            {
                b.HasIndex(r => new { r.CookbookId, r.Status, r.CreatedAt });
                b.HasIndex(r => new { r.CookbookId, r.RecipeId, r.RequestedByUserId, r.Status });

                b.Property(r => r.Message).HasMaxLength(600);

                b.HasOne(r => r.Cookbook)
                    .WithMany()
                    .HasForeignKey(r => r.CookbookId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(r => r.Recipe)
                    .WithMany()
                    .HasForeignKey(r => r.RecipeId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(r => r.RequestedByUser)
                    .WithMany()
                    .HasForeignKey(r => r.RequestedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(r => r.ReviewedByUser)
                    .WithMany()
                    .HasForeignKey(r => r.ReviewedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.Property(r => r.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            });

            // ---------- Allergies / Ingredients normalization ----------
            modelBuilder.Entity<IngredientAllergen>().HasKey(ia => new { ia.IngredientId, ia.AllergenType });
            modelBuilder.Entity<IngredientAllergen>()
                .HasOne(ia => ia.Ingredient)
                .WithMany(i => i.Allergens)
                .HasForeignKey(ia => ia.IngredientId);

            modelBuilder.Entity<IngredientAllergen>()
                .Property(ia => ia.AllergenType)
                .HasMaxLength(64)
                .IsUnicode(false);

            // ✅ PROD table IngredientAllergens has varchar column named "Severity"
            modelBuilder.Entity<IngredientAllergen>()
                .Property(ia => ia.Severity)
                .HasColumnName("Severity")
                .HasConversion<string>();

            modelBuilder.Entity<IngredientAllergen>()
                .Property(ia => ia.MayContain);

            modelBuilder.Entity<IngredientAllergen>()
                .Property(ia => ia.CrossContactRisk);

            modelBuilder.Entity<IngredientAllergen>()
                .Property(ia => ia.ProcessingAid);

            modelBuilder.Entity<GlobalIngredientAllergen>().HasKey(ga => new { ga.GlobalIngredientId, ga.AllergenType });
            modelBuilder.Entity<GlobalIngredientAllergen>()
                .HasOne(ga => ga.GlobalIngredient)
                .WithMany(g => g.Allergens)
                .HasForeignKey(ga => ga.GlobalIngredientId);

            modelBuilder.Entity<GlobalIngredientAllergen>()
                .Property(ga => ga.AllergenType)
                .HasMaxLength(64)
                .IsUnicode(false);

            // ✅ PROD table GlobalIngredientAllergens has INT column named "SeverityLevel"
            modelBuilder.Entity<GlobalIngredientAllergen>()
                .Property(ga => ga.Severity)
                .HasColumnName("SeverityLevel")
                .HasConversion<int>()
                .HasDefaultValue(AllergenSeverity.Low);

            modelBuilder.Entity<GlobalIngredientAllergen>()
                .Property(ga => ga.MayContain)
                .HasDefaultValue(false);

            modelBuilder.Entity<GlobalIngredientAllergen>()
                .Property(ga => ga.CrossContactRisk)
                .HasDefaultValue(false);

            modelBuilder.Entity<GlobalIngredientAllergen>()
                .Property(ga => ga.ProcessingAid)
                .HasDefaultValue(false);

            modelBuilder.Entity<GlobalIngredientAllergen>()
                .Property(ga => ga.ENumber)
                .HasMaxLength(16)
                .IsUnicode(false);

            modelBuilder.Entity<GlobalIngredientAllergen>()
                .Property(ga => ga.Notes)
                .HasMaxLength(256);

            // ---------- UserAllergy (join: Users ↔ Allergies) ----------
            modelBuilder.Entity<UserAllergy>().HasKey(ua => new { ua.UserId, ua.AllergyId });

            modelBuilder.Entity<UserAllergy>()
                .HasOne(ua => ua.User)
                .WithMany(u => u.Allergies)
                .HasForeignKey(ua => ua.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserAllergy>()
                .HasOne(ua => ua.Allergy)
                .WithMany(a => a.UserAllergies)
                .HasForeignKey(ua => ua.AllergyId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserAllergy>()
                .Property(ua => ua.IsEnabled).HasDefaultValue(true);

            modelBuilder.Entity<UserAllergy>()
                .Property(ua => ua.IncludeTrace).HasDefaultValue(true);

            modelBuilder.Entity<UserAllergy>()
                .Property(ua => ua.MinSeverity).HasConversion<string>().HasMaxLength(8).IsUnicode(false);

            // ---------- Trending Cache ----------
            modelBuilder.Entity<RecipeTrendingCache>(b =>
            {
                b.HasKey(x => x.RecipeId);
                b.HasIndex(x => x.Score);
                b.HasIndex(x => x.Rank);
                b.HasIndex(x => x.VelocityScore);
            });

            // ---------- Recipe Views (for trending) ----------
            modelBuilder.Entity<RecipeView>(b =>
            {
                b.HasIndex(x => new { x.RecipeId, x.ViewedAt });
                b.HasIndex(x => x.ViewedAt);
                b.HasIndex(x => new { x.RecipeId, x.IpHash, x.ViewedAt }); // For deduplication

                b.Property(x => x.IpHash).HasMaxLength(64).IsUnicode(false);
                b.Property(x => x.UserAgent).HasMaxLength(512);
                b.Property(x => x.Referrer).HasMaxLength(1024);
                b.Property(x => x.ViewedAt).HasDefaultValueSql("SYSUTCDATETIME()");

                b.HasOne(x => x.Recipe)
                    .WithMany()
                    .HasForeignKey(x => x.RecipeId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.User)
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.NoAction); // Avoid cascade conflicts
            });

            // ---------- Recipe Trending History ----------
            modelBuilder.Entity<RecipeTrendingHistory>(b =>
            {
                b.HasIndex(x => new { x.RecipeId, x.ComputedAt });
                b.HasIndex(x => x.ComputedAt);

                b.Property(x => x.ComputedAt).HasDefaultValueSql("SYSUTCDATETIME()");

                b.HasOne(x => x.Recipe)
                    .WithMany()
                    .HasForeignKey(x => x.RecipeId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ---------- User Trending Cache ----------
            modelBuilder.Entity<UserTrendingCache>(b =>
            {
                b.HasKey(x => x.UserId);
                b.HasIndex(x => x.Score);
                b.HasIndex(x => x.Rank);

                b.Property(x => x.ComputedAt).HasDefaultValueSql("SYSUTCDATETIME()");

                b.HasOne(x => x.User)
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ---------- Refresh Tokens ----------
            modelBuilder.Entity<RefreshToken>(b =>
            {
                b.HasIndex(r => r.TokenHash).IsUnique();
                b.HasIndex(r => new { r.UserId, r.RevokedAtUtc, r.ExpiresAtUtc });
                b.HasIndex(r => r.ReplacedByTokenHash);

                b.HasOne(r => r.User)
                    .WithMany()
                    .HasForeignKey(r => r.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ---------- Allergen Lexicon ----------
            modelBuilder.Entity<AllergenKeyword>(b =>
            {
                b.HasIndex(x => new { x.Text, x.Kind }).IsUnique();
                b.Property(x => x.Text).HasMaxLength(128).IsRequired();
                b.Property(x => x.ENumber).HasMaxLength(16);
                b.Property(x => x.Notes).HasMaxLength(256);
                b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
            });

            modelBuilder.Entity<AllergenKeywordAllergy>(b =>
            {
                b.HasKey(x => new { x.AllergenKeywordId, x.AllergyId });

                b.HasOne(x => x.AllergenKeyword)
                    .WithMany(k => k.Allergies)
                    .HasForeignKey(x => x.AllergenKeywordId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.Allergy)
                    .WithMany()
                    .HasForeignKey(x => x.AllergyId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ---------- Billing Email Events ----------
            modelBuilder.Entity<BillingEmailEvent>(b =>
            {
                b.HasIndex(x => new { x.StripeEventId, x.EventType }).IsUnique();
                b.HasIndex(x => x.UserId);
            });

            // ---------- Prohibited Ingredients (Safety) ----------
            modelBuilder.Entity<ProhibitedIngredient>(b =>
            {
                b.HasIndex(x => x.Keyword);
                b.HasIndex(x => x.Category);
                b.HasIndex(x => x.IsActive);

                b.Property(x => x.Keyword).HasMaxLength(256).IsRequired();
                b.Property(x => x.Reason).HasMaxLength(512);
                b.Property(x => x.AdminNotes).HasMaxLength(1024);
                b.Property(x => x.Category).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
                b.Property(x => x.MatchType).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
                b.Property(x => x.Severity).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
                b.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            });

            // ---------- Ingredient Categories (Taxonomy) ----------
            modelBuilder.Entity<IngredientCategory>(b =>
            {
                b.HasIndex(x => x.Name);
                b.HasIndex(x => x.Slug).IsUnique();
                b.HasIndex(x => x.ParentCategoryId);

                b.Property(x => x.Name).HasMaxLength(128).IsRequired();
                b.Property(x => x.Slug).HasMaxLength(128).IsRequired();
                b.Property(x => x.Description).HasMaxLength(512);
                b.Property(x => x.IconName).HasMaxLength(64);
                b.Property(x => x.CommonAllergens).HasMaxLength(256);
                b.Property(x => x.DietaryFlags).HasMaxLength(256);

                b.HasOne(x => x.ParentCategory)
                    .WithMany(x => x.ChildCategories)
                    .HasForeignKey(x => x.ParentCategoryId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // ---------- GlobalIngredient (B2B updates) ----------
            modelBuilder.Entity<GlobalIngredient>(b =>
            {
                b.Property(x => x.NormalizedName).HasMaxLength(256);
                b.Property(x => x.Aliases).HasMaxLength(512);
                b.Property(x => x.PrimaryRegion).HasMaxLength(64);
                b.Property(x => x.DietaryFlags).HasMaxLength(256);
                b.Property(x => x.NutritionalCategory).HasMaxLength(64);
                b.Property(x => x.AverageRecipeRating).HasPrecision(3, 2);
                b.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

                b.HasOne(x => x.Category)
                    .WithMany(c => c.Ingredients)
                    .HasForeignKey(x => x.CategoryId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // ---------- Ingredient Substitutions ----------
            modelBuilder.Entity<IngredientSubstitution>(b =>
            {
                b.HasIndex(x => new { x.OriginalIngredientId, x.SubstituteIngredientId }).IsUnique();
                b.HasIndex(x => x.Reason);
                b.HasIndex(x => x.ConfidenceScore);

                b.Property(x => x.Notes).HasMaxLength(512);
                b.Property(x => x.ConversionRatio).HasPrecision(6, 3);
                b.Property(x => x.Reason).HasConversion<string>().HasMaxLength(24).IsUnicode(false);
                b.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

                b.HasOne(x => x.OriginalIngredient)
                    .WithMany(g => g.SubstitutionsAsOriginal)
                    .HasForeignKey(x => x.OriginalIngredientId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(x => x.SubstituteIngredient)
                    .WithMany(g => g.SubstitutionsAsSubstitute)
                    .HasForeignKey(x => x.SubstituteIngredientId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(x => x.Allergy)
                    .WithMany()
                    .HasForeignKey(x => x.AllergyId)
                    .OnDelete(DeleteBehavior.SetNull);

                b.HasOne(x => x.SuggestedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.SuggestedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // ---------- Substitution Votes ----------
            modelBuilder.Entity<SubstitutionVote>(b =>
            {
                b.HasKey(x => new { x.UserId, x.SubstitutionId });

                b.Property(x => x.Comment).HasMaxLength(256);
                b.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

                b.HasOne(x => x.User)
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.Substitution)
                    .WithMany()
                    .HasForeignKey(x => x.SubstitutionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ---------- Ingredient Usage Log (B2B Analytics) ----------
            modelBuilder.Entity<IngredientUsageLog>(b =>
            {
                b.Property(x => x.Region).HasMaxLength(64);
                b.Property(x => x.CountryCode).HasMaxLength(2).IsUnicode(false);
                b.Property(x => x.City).HasMaxLength(128);
                b.Property(x => x.Quantity).HasMaxLength(64);
                b.Property(x => x.Unit).HasMaxLength(32);
                b.Property(x => x.QuantityNumeric).HasPrecision(10, 3);
                b.Property(x => x.DietaryTags).HasMaxLength(256);
                b.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

                b.HasOne(x => x.GlobalIngredient)
                    .WithMany()
                    .HasForeignKey(x => x.GlobalIngredientId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(x => x.Recipe)
                    .WithMany()
                    .HasForeignKey(x => x.RecipeId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ---------- Ingredient Trend Daily ----------
            modelBuilder.Entity<IngredientTrendDaily>(b =>
            {
                b.Property(x => x.Region).HasMaxLength(64);
                b.Property(x => x.DayOverDayChange).HasPrecision(8, 4);
                b.Property(x => x.WeekOverWeekChange).HasPrecision(8, 4);
                b.Property(x => x.ComputedAt).HasDefaultValueSql("SYSUTCDATETIME()");

                b.HasOne(x => x.GlobalIngredient)
                    .WithMany()
                    .HasForeignKey(x => x.GlobalIngredientId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ---------- Regional Ingredient Stats ----------
            modelBuilder.Entity<RegionalIngredientStats>(b =>
            {
                b.Property(x => x.Region).HasMaxLength(64).IsRequired();
                b.Property(x => x.CommonUnit).HasMaxLength(32);
                b.Property(x => x.AverageQuantity).HasPrecision(10, 3);
                b.Property(x => x.PenetrationRate).HasPrecision(5, 4);
                b.Property(x => x.SeasonalPeakMonths).HasMaxLength(32);
                b.Property(x => x.CommonPairings).HasMaxLength(512);
                b.Property(x => x.CommonDietaryContexts).HasMaxLength(256);
                b.Property(x => x.LastUpdated).HasDefaultValueSql("SYSUTCDATETIME()");

                b.HasOne(x => x.GlobalIngredient)
                    .WithMany()
                    .HasForeignKey(x => x.GlobalIngredientId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ---------- Ingredient Pairings ----------
            modelBuilder.Entity<IngredientPairing>(b =>
            {
                b.Property(x => x.AffinityScore).HasPrecision(5, 4);
                b.Property(x => x.TopRegion).HasMaxLength(64);
                b.Property(x => x.CommonCuisines).HasMaxLength(256);
                b.Property(x => x.LastUpdated).HasDefaultValueSql("SYSUTCDATETIME()");

                b.HasOne(x => x.IngredientA)
                    .WithMany()
                    .HasForeignKey(x => x.IngredientAId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(x => x.IngredientB)
                    .WithMany()
                    .HasForeignKey(x => x.IngredientBId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // ---------- Dietary Trends ----------
            modelBuilder.Entity<DietaryTrend>(b =>
            {
                b.Property(x => x.DietaryTag).HasMaxLength(64).IsRequired();
                b.Property(x => x.Region).HasMaxLength(64);
                b.Property(x => x.GrowthRate).HasPrecision(8, 4);
                b.Property(x => x.MarketShare).HasPrecision(5, 4);
                b.Property(x => x.ComputedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            });

            // ---------- Ingredient Search Log ----------
            modelBuilder.Entity<IngredientSearchLog>(b =>
            {
                b.Property(x => x.Query).HasMaxLength(256).IsRequired();
                b.Property(x => x.Region).HasMaxLength(64);
                b.Property(x => x.SearchContext).HasMaxLength(32);
                b.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            });

            // ---------- Shopping Lists ----------
            modelBuilder.Entity<ShoppingList>(b =>
            {
                b.HasIndex(x => x.UserId);
                b.HasIndex(x => x.ShareToken).IsUnique().HasFilter("[ShareToken] IS NOT NULL");
                b.HasIndex(x => x.IsDeleted);
                b.HasIndex(x => x.MealPlanId);

                b.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

                b.HasOne(x => x.User)
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(x => x.MealPlan)
                    .WithMany(mp => mp.ShoppingLists)
                    .HasForeignKey(x => x.MealPlanId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<ShoppingListItem>(b =>
            {
                b.HasIndex(x => x.ShoppingListId);
                b.HasIndex(x => x.GlobalIngredientId);
                b.HasIndex(x => new { x.ShoppingListId, x.SortOrder });

                b.Property(x => x.NumericAmount).HasPrecision(10, 3);
                b.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

                b.HasOne(x => x.ShoppingList)
                    .WithMany(sl => sl.Items)
                    .HasForeignKey(x => x.ShoppingListId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.GlobalIngredient)
                    .WithMany()
                    .HasForeignKey(x => x.GlobalIngredientId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<ShoppingListItemSource>(b =>
            {
                b.HasIndex(x => x.ShoppingListItemId);
                b.HasIndex(x => x.RecipeId);

                b.Property(x => x.ScaleFactor).HasPrecision(6, 3);
                b.Property(x => x.NumericAmount).HasPrecision(10, 3);

                b.HasOne(x => x.ShoppingListItem)
                    .WithMany(i => i.Sources)
                    .HasForeignKey(x => x.ShoppingListItemId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.Recipe)
                    .WithMany()
                    .HasForeignKey(x => x.RecipeId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ShoppingListRecipe>(b =>
            {
                b.HasIndex(x => new { x.ShoppingListId, x.RecipeId }).IsUnique();

                b.Property(x => x.ScaleFactor).HasPrecision(6, 3);
                b.Property(x => x.AddedAt).HasDefaultValueSql("SYSUTCDATETIME()");

                b.HasOne(x => x.ShoppingList)
                    .WithMany(sl => sl.SourceRecipes)
                    .HasForeignKey(x => x.ShoppingListId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.Recipe)
                    .WithMany()
                    .HasForeignKey(x => x.RecipeId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // ---------- Meal Plans ----------
            modelBuilder.Entity<MealPlan>(b =>
            {
                b.HasIndex(x => x.UserId);
                b.HasIndex(x => x.ShareToken).IsUnique().HasFilter("[ShareToken] IS NOT NULL");
                b.HasIndex(x => x.IsDeleted);
                b.HasIndex(x => new { x.StartDate, x.EndDate });

                b.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

                b.HasOne(x => x.User)
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<MealPlanEntry>(b =>
            {
                b.HasIndex(x => x.MealPlanId);
                b.HasIndex(x => x.RecipeId);
                b.HasIndex(x => new { x.MealPlanId, x.Date, x.MealType });

                b.Property(x => x.ScaleFactor).HasPrecision(6, 3);
                b.Property(x => x.MealType).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
                b.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

                b.HasOne(x => x.MealPlan)
                    .WithMany(mp => mp.Entries)
                    .HasForeignKey(x => x.MealPlanId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.Recipe)
                    .WithMany()
                    .HasForeignKey(x => x.RecipeId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // ---------- API Keys ----------
            modelBuilder.Entity<ApiKey>(b =>
            {
                b.HasIndex(x => x.KeyPrefix);
                b.HasIndex(x => x.KeyHash);
                b.HasIndex(x => x.UserId);
                b.HasIndex(x => new { x.KeyPrefix, x.KeyHash });

                b.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

                b.HasOne(x => x.User)
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ApiKeyUsageLog>(b =>
            {
                b.HasIndex(x => x.ApiKeyId);
                b.HasIndex(x => x.CreatedAt);
                b.HasIndex(x => new { x.ApiKeyId, x.CreatedAt });

                b.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

                b.HasOne(x => x.ApiKey)
                    .WithMany()
                    .HasForeignKey(x => x.ApiKeyId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ---------- Allergen Scan Quota ----------
            modelBuilder.Entity<UserAllergenScan>(b =>
            {
                // Unique: one row per user+recipe+week (prevents double-counting)
                b.HasIndex(x => new { x.UserId, x.RecipeId, x.WeekStartDate }).IsUnique();
                // Fast weekly count queries
                b.HasIndex(x => new { x.UserId, x.WeekStartDate });

                b.Property(x => x.WeekStartDate).HasColumnType("date");
                b.Property(x => x.ScannedAt).HasDefaultValueSql("SYSUTCDATETIME()");

                b.HasOne(x => x.User)
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

        }
    }
}
