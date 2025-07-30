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


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Follow system
            modelBuilder.Entity<UserFollow>()
                .HasKey(uf => new { uf.FollowerId, uf.FollowingId });

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

            // Likes system (Fix multiple cascade paths)
            modelBuilder.Entity<RecipeLike>()
                .HasKey(rl => new { rl.UserId, rl.RecipeId });

            modelBuilder.Entity<RecipeLike>()
                .HasOne(rl => rl.User)
                .WithMany()
                .HasForeignKey(rl => rl.UserId)
                .OnDelete(DeleteBehavior.Restrict); // <== IMPORTANT

            modelBuilder.Entity<RecipeLike>()
                .HasOne(rl => rl.Recipe)
                .WithMany(r => r.Likes)
                .HasForeignKey(rl => rl.RecipeId)
                .OnDelete(DeleteBehavior.Restrict); // <== IMPORTANT

            // Comments relationship
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

            //repost
            modelBuilder.Entity<RecipeRepost>()
                .HasKey(rr => new { rr.UserId, rr.RecipeId });

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

            // Remix system
            modelBuilder.Entity<RecipeRemix>()
                .HasKey(rr => rr.Id);

            modelBuilder.Entity<RecipeRemix>()
                .HasOne(rr => rr.RemixRecipe)
                .WithOne()
                .HasForeignKey<RecipeRemix>(rr => rr.RemixRecipeId)
                .OnDelete(DeleteBehavior.Restrict); // Deleting a remix deletes this relationship

            modelBuilder.Entity<RecipeRemix>()
                .HasOne(rr => rr.OriginalRecipe)
                .WithMany()
                .HasForeignKey(rr => rr.OriginalRecipeId)
                .OnDelete(DeleteBehavior.Restrict); // If original is deleted, keep remix but set OriginalRecipeId to null

            //Cookbook

            modelBuilder.Entity<CookbookRecipe>()
                .HasKey(cr => new { cr.CookbookId, cr.RecipeId });

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


            modelBuilder.Entity<UserAllergy>()
                .HasKey(ua => new { ua.UserId, ua.AllergyId });

            modelBuilder.Entity<UserAllergy>()
                .HasOne(ua => ua.User)
                .WithMany(u => u.Allergies)
                .HasForeignKey(ua => ua.UserId);

            modelBuilder.Entity<UserAllergy>()
                .HasOne(ua => ua.Allergy)
                .WithMany(a => a.UserAllergies)
                .HasForeignKey(ua => ua.AllergyId);

        }
    }
}
