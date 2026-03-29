using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platerly.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipeIndexEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecipeIndexSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SourceDomain = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ApiEndpoint = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    LastIndexedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalRecipesIndexed = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    TotalRecipesDiscarded = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    RequiresApiKey = table.Column<bool>(type: "bit", nullable: false),
                    ApiKeyReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LegalNotes = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    RespectRobotsTxt = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    RequestDelayMs = table.Column<int>(type: "int", nullable: false, defaultValue: 1000),
                    AllowsThumbnailDisplay = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeIndexSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecipeIndex",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    SourceDomain = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OriginalAuthor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IngredientsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InstructionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IndexedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    LastCheckedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AllergenAnalysisJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NickelLoadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NutritionJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsAnalysed = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AnalysisVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ThumbnailUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ServingSize = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PrepTimeMinutes = table.Column<int>(type: "int", nullable: true),
                    CookTimeMinutes = table.Column<int>(type: "int", nullable: true),
                    CuisineType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    TagsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeIndex", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecipeIndex_RecipeIndexSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "RecipeIndexSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIndex_CuisineType",
                table: "RecipeIndex",
                column: "CuisineType");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIndex_IndexedAt",
                table: "RecipeIndex",
                column: "IndexedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIndex_IsAnalysed",
                table: "RecipeIndex",
                column: "IsAnalysed");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIndex_IsAnalysed_IndexedAt",
                table: "RecipeIndex",
                columns: new[] { "IsAnalysed", "IndexedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIndex_SourceDomain",
                table: "RecipeIndex",
                column: "SourceDomain");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIndex_SourceId",
                table: "RecipeIndex",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIndex_SourceUrl",
                table: "RecipeIndex",
                column: "SourceUrl",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIndexSources_IsActive",
                table: "RecipeIndexSources",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIndexSources_SourceDomain",
                table: "RecipeIndexSources",
                column: "SourceDomain",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecipeIndex");

            migrationBuilder.DropTable(
                name: "RecipeIndexSources");
        }
    }
}
