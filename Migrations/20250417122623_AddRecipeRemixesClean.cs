using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platerly.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipeRemixesClean : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecipeRemixes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RemixRecipeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalRecipeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RemixedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeRemixes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecipeRemixes_Recipes_OriginalRecipeId",
                        column: x => x.OriginalRecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecipeRemixes_Recipes_RemixRecipeId",
                        column: x => x.RemixRecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeRemixes_OriginalRecipeId",
                table: "RecipeRemixes",
                column: "OriginalRecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeRemixes_RemixRecipeId",
                table: "RecipeRemixes",
                column: "RemixRecipeId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecipeRemixes");
        }
    }
}
