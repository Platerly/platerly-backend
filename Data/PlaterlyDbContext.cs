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
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<AllergenKeyword> AllergenKeywords { get; set; } = null!;
        public DbSet<AllergenKeywordAllergy> AllergenKeywordAllergies { get; set; } = null!;
        public DbSet<BillingEmailEvent> BillingEmailEvents { get; set; }


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
            modelBuilder.Entity<UserFollow>().HasKey(uf => new { uf.FollowerId, uf.FollowingId });

            modelBuilder.Entity<UserFollow>()
                .HasOne(uf => uf.Follower)
                .WithMany(u => u.Following)
                .HasForeignKey(uf => uf.FollowerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<UserFollow>()
                .HasOne(uf => uf.Following)
                .WithMany(u => u.Followers)
                .HasForeignKey(uf => uf.FollowingId)
                .OnDelete(DeleteBehavior.Restrict);

            // ---------- Likes ----------
            modelBuilder.Entity(typeof(RecipeLike)).HasKey("UserId", "RecipeId");

            modelBuilder.Entity<RecipeLike>()
                .HasOne(rl => rl.User)
                .WithMany()
                .HasForeignKey(rl => rl.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<RecipeLike>()
                .HasOne(rl => rl.Recipe)
                .WithMany(r => r.Likes)
                .HasForeignKey(rl => rl.RecipeId)
                .OnDelete(DeleteBehavior.Restrict);

            // ---------- Comments ----------
            modelBuilder.Entity<Comment>()
                .HasOne(c => c.User)
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Comment>()
                .HasOne(c => c.Recipe)
                .WithMany()
                .HasForeignKey(c => c.RecipeId)
                .OnDelete(DeleteBehavior.Cascade);

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

        }
    }
}
