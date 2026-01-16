using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platerly.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_UserFollows_FollowerId",
                table: "UserFollows",
                column: "FollowerId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeLikes_UserId",
                table: "RecipeLikes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_RecipeId_CreatedAt",
                table: "Comments",
                columns: new[] { "RecipeId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserFollows_FollowerId",
                table: "UserFollows");

            migrationBuilder.DropIndex(
                name: "IX_RecipeLikes_UserId",
                table: "RecipeLikes");

            migrationBuilder.DropIndex(
                name: "IX_Comments_RecipeId_CreatedAt",
                table: "Comments");
        }
    }
}
