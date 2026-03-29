using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platerly.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCookbookIndexedRecipes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CookbookIndexedRecipes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CookbookId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipeIndexId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AllergenSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SavedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SavedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    Note = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CookbookIndexedRecipes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CookbookIndexedRecipes_Cookbooks_CookbookId",
                        column: x => x.CookbookId,
                        principalTable: "Cookbooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CookbookIndexedRecipes_RecipeIndex_RecipeIndexId",
                        column: x => x.RecipeIndexId,
                        principalTable: "RecipeIndex",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CookbookIndexedRecipes_Users_SavedByUserId",
                        column: x => x.SavedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CookbookIndexedRecipes_CookbookId_RecipeIndexId",
                table: "CookbookIndexedRecipes",
                columns: new[] { "CookbookId", "RecipeIndexId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CookbookIndexedRecipes_RecipeIndexId",
                table: "CookbookIndexedRecipes",
                column: "RecipeIndexId");

            migrationBuilder.CreateIndex(
                name: "IX_CookbookIndexedRecipes_SavedByUserId",
                table: "CookbookIndexedRecipes",
                column: "SavedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CookbookIndexedRecipes");
        }
    }
}
