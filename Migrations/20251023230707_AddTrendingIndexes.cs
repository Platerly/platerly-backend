using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platerly.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTrendingIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Likes
            migrationBuilder.Sql(@"
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RecipeLikes_Recipe_LikedAt')
        CREATE INDEX IX_RecipeLikes_Recipe_LikedAt
        ON RecipeLikes(RecipeId, LikedAt DESC);
    ");

            // Reposts
            migrationBuilder.Sql(@"
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RecipeReposts_Recipe_RepostedAt')
        CREATE INDEX IX_RecipeReposts_Recipe_RepostedAt
        ON RecipeReposts(RecipeId, RepostedAt DESC);
    ");

            // Remixes
            migrationBuilder.Sql(@"
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RecipeRemixes_Parent_RemixedAt')
        CREATE INDEX IX_RecipeRemixes_Parent_RemixedAt
        ON RecipeRemixes(ParentRecipeId, RemixedAt DESC);
    ");

            // Follows
            migrationBuilder.Sql(@"
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserFollows_FollowingId')
        CREATE INDEX IX_UserFollows_FollowingId
        ON UserFollows(FollowingId);
    ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RecipeLikes_Recipe_LikedAt')
        DROP INDEX IX_RecipeLikes_Recipe_LikedAt ON RecipeLikes;

        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RecipeReposts_Recipe_RepostedAt')
        DROP INDEX IX_RecipeReposts_Recipe_RepostedAt ON RecipeReposts;

        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RecipeRemixes_Parent_RemixedAt')
        DROP INDEX IX_RecipeRemixes_Parent_RemixedAt ON RecipeRemixes;

        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserFollows_FollowingId')
        DROP INDEX IX_UserFollows_FollowingId ON UserFollows;
    ");
        }

    }
}
