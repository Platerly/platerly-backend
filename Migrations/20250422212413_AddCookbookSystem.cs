using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platerly.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCookbookSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Cookbook_Users_UserId",
                table: "Cookbook");

            migrationBuilder.DropForeignKey(
                name: "FK_Recipes_Cookbook_CookbookId",
                table: "Recipes");

            migrationBuilder.DropIndex(
                name: "IX_Recipes_CookbookId",
                table: "Recipes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Cookbook",
                table: "Cookbook");

            migrationBuilder.DropColumn(
                name: "CookbookId",
                table: "Recipes");

            migrationBuilder.RenameTable(
                name: "Cookbook",
                newName: "Cookbooks");

            migrationBuilder.RenameIndex(
                name: "IX_Cookbook_UserId",
                table: "Cookbooks",
                newName: "IX_Cookbooks_UserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Cookbooks",
                table: "Cookbooks",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "CookbookRecipes",
                columns: table => new
                {
                    CookbookId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CookbookRecipes", x => new { x.CookbookId, x.RecipeId });
                    table.ForeignKey(
                        name: "FK_CookbookRecipes_Cookbooks_CookbookId",
                        column: x => x.CookbookId,
                        principalTable: "Cookbooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CookbookRecipes_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CookbookRecipes_RecipeId",
                table: "CookbookRecipes",
                column: "RecipeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Cookbooks_Users_UserId",
                table: "Cookbooks",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.NoAction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Cookbooks_Users_UserId",
                table: "Cookbooks");

            migrationBuilder.DropTable(
                name: "CookbookRecipes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Cookbooks",
                table: "Cookbooks");

            migrationBuilder.RenameTable(
                name: "Cookbooks",
                newName: "Cookbook");

            migrationBuilder.RenameIndex(
                name: "IX_Cookbooks_UserId",
                table: "Cookbook",
                newName: "IX_Cookbook_UserId");

            migrationBuilder.AddColumn<Guid>(
                name: "CookbookId",
                table: "Recipes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Cookbook",
                table: "Cookbook",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_CookbookId",
                table: "Recipes",
                column: "CookbookId");

            migrationBuilder.AddForeignKey(
                name: "FK_Cookbook_Users_UserId",
                table: "Cookbook",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Recipes_Cookbook_CookbookId",
                table: "Recipes",
                column: "CookbookId",
                principalTable: "Cookbook",
                principalColumn: "Id");
        }
    }
}
