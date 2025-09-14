using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platerly.Api.Migrations
{
    /// <inheritdoc />
    public partial class CookbookRecipe_Order_And_Note : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CoverImageBlobName",
                table: "Cookbooks",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverImageUrl",
                table: "Cookbooks",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Cookbooks",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Cookbooks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AddedByUserId",
                table: "CookbookRecipes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "CookbookRecipes",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "CookbookRecipes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_CookbookRecipes_AddedByUserId",
                table: "CookbookRecipes",
                column: "AddedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CookbookRecipes_CookbookId_SortOrder",
                table: "CookbookRecipes",
                columns: new[] { "CookbookId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CookbookRecipes_AddedByUserId",
                table: "CookbookRecipes");

            migrationBuilder.DropIndex(
                name: "IX_CookbookRecipes_CookbookId_SortOrder",
                table: "CookbookRecipes");

            migrationBuilder.DropColumn(
                name: "CoverImageBlobName",
                table: "Cookbooks");

            migrationBuilder.DropColumn(
                name: "CoverImageUrl",
                table: "Cookbooks");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Cookbooks");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Cookbooks");

            migrationBuilder.DropColumn(
                name: "AddedByUserId",
                table: "CookbookRecipes");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "CookbookRecipes");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "CookbookRecipes");
        }
    }
}
