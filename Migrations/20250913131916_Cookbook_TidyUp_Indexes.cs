using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platerly.Api.Migrations
{
    /// <inheritdoc />
    public partial class Cookbook_TidyUp_Indexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CookbookRecipes_Cookbooks_CookbookId",
                table: "CookbookRecipes");

            migrationBuilder.CreateIndex(
                name: "IX_Cookbooks_IsPublic",
                table: "Cookbooks",
                column: "IsPublic");

            migrationBuilder.CreateIndex(
                name: "IX_CookbookContributionRequests_CookbookId_RecipeId_RequestedByUserId_Status",
                table: "CookbookContributionRequests",
                columns: new[] { "CookbookId", "RecipeId", "RequestedByUserId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_CookbookRecipes_Cookbooks_CookbookId",
                table: "CookbookRecipes",
                column: "CookbookId",
                principalTable: "Cookbooks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CookbookRecipes_Cookbooks_CookbookId",
                table: "CookbookRecipes");

            migrationBuilder.DropIndex(
                name: "IX_Cookbooks_IsPublic",
                table: "Cookbooks");

            migrationBuilder.DropIndex(
                name: "IX_CookbookContributionRequests_CookbookId_RecipeId_RequestedByUserId_Status",
                table: "CookbookContributionRequests");

            migrationBuilder.AddForeignKey(
                name: "FK_CookbookRecipes_Cookbooks_CookbookId",
                table: "CookbookRecipes",
                column: "CookbookId",
                principalTable: "Cookbooks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
