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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ---------- Global Filters ----------
            // Hide soft-deleted recipes by default across all queries
            modelBuilder.Entity<Recipe>().HasQueryFilter(r => !r.IsDeleted);

            // ---------- Users ----------
            modelBuilder.Entity<User>(b =>
            {
                // Uniqueness & lengths commonly enforced at scale
                b.HasIndex(u => u.Username).IsUnique();
                b.HasIndex(u => u.Email).IsUnique();

                b.Property(u => u.Username).HasMaxLength(64).IsRequired();
                b.Property(u => u.Email).HasMaxLength(256).IsRequired();
                b.Property(u => u.DisplayName).HasMaxLength(128);
                b.Property(u => u.Bio).HasMaxLength(1024);

                b.Property(u => u.ProfileImageUrl).HasMaxLength(1024);
                b.Property(u => u.ProfileImageBlobName).HasMaxLength(256);

                // Concurrency token (optimistic concurrency)
                b.Property(u => u.RowVersion).IsRowVersion();

                // CreatedAt default (UTC)
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
                b.Property(r => r.Tags).HasMaxLength(512);

                b.Property(r => r.Difficulty).HasMaxLength(16);
                b.Property(r => r.IsDeleted).HasDefaultValue(false);
                b.Property(r => r.UpdatedAt);
                b.Property(r => r.DeletedAt);
                b.Property(r => r.DeletedByUserId);

                // Concurrency token
                b.Property(r => r.RowVersion).IsRowVersion();

                // CreatedAt default (UTC)
                b.Property(r => r.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
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
            // Keep Restrict since we soft-delete originals (Twitter/X-like “unavailable”)
            // Use IgnoreQueryFilters() when checking parent availability.

            // ---------- Cookbooks ----------
            modelBuilder.Entity<CookbookRecipe>().HasKey(cr => new { cr.CookbookId, cr.RecipeId });

            modelBuilder.Entity<CookbookRecipe>()
                .HasOne(cr => cr.Cookbook)
                .WithMany(c => c.Recipes)
                .HasForeignKey(cr => cr.CookbookId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CookbookRecipe>()
                .HasOne(cr => cr.Recipe)
                .WithMany()
                .HasForeignKey(cr => cr.RecipeId)
                .OnDelete(DeleteBehavior.Restrict);

            // ---------- Allergies / Ingredients normalization ----------
            modelBuilder.Entity<IngredientAllergen>().HasKey(ia => new { ia.IngredientId, ia.AllergenType });
            modelBuilder.Entity<IngredientAllergen>()
                .HasOne(ia => ia.Ingredient)
                .WithMany(i => i.Allergens)
                .HasForeignKey(ia => ia.IngredientId);

            modelBuilder.Entity<GlobalIngredientAllergen>().HasKey(ga => new { ga.GlobalIngredientId, ga.AllergenType });
            modelBuilder.Entity<GlobalIngredientAllergen>()
                .HasOne(ga => ga.GlobalIngredient)
                .WithMany(g => g.Allergens)
                .HasForeignKey(ga => ga.GlobalIngredientId);

            // ---------- UserAllergy (join: Users ↔ Allergies) ----------
            modelBuilder.Entity<UserAllergy>().HasKey(ua => new { ua.UserId, ua.AllergyId });

            modelBuilder.Entity<UserAllergy>()
                .HasOne(ua => ua.User)
                .WithMany(u => u.Allergies)
                .HasForeignKey(ua => ua.UserId)
                .OnDelete(DeleteBehavior.Cascade); // or Restrict if you prefer

            modelBuilder.Entity<UserAllergy>()
                .HasOne(ua => ua.Allergy)
                .WithMany(a => a.UserAllergies)
                .HasForeignKey(ua => ua.AllergyId)
                .OnDelete(DeleteBehavior.Cascade); // or Restrict if you prefer

        }
    }
}
