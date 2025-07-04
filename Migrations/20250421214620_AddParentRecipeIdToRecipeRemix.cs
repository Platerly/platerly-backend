using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platerly.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddParentRecipeIdToRecipeRemix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentRecipeId",
                table: "RecipeRemixes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecipeRemixes_ParentRecipeId",
                table: "RecipeRemixes",
                column: "ParentRecipeId");

            migrationBuilder.AddForeignKey(
                name: "FK_RecipeRemixes_Recipes_ParentRecipeId",
                table: "RecipeRemixes",
                column: "ParentRecipeId",
                principalTable: "Recipes",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecipeRemixes_Recipes_ParentRecipeId",
                table: "RecipeRemixes");

            migrationBuilder.DropIndex(
                name: "IX_RecipeRemixes_ParentRecipeId",
                table: "RecipeRemixes");

            migrationBuilder.DropColumn(
                name: "ParentRecipeId",
                table: "RecipeRemixes");
        }
    }
}
