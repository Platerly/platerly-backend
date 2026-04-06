using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platerly.Api.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeIngredientAliases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GlobalIngredientAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GlobalIngredientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Alias = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NormalizedAlias = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalIngredientAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GlobalIngredientAliases_GlobalIngredients_GlobalIngredientId",
                        column: x => x.GlobalIngredientId,
                        principalTable: "GlobalIngredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GlobalIngredientAliases_GlobalIngredientId",
                table: "GlobalIngredientAliases",
                column: "GlobalIngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_GlobalIngredientAliases_NormalizedAlias",
                table: "GlobalIngredientAliases",
                column: "NormalizedAlias");

            // Migrate existing comma-separated Aliases data into the new table.
            // STRING_SPLIT is available in SQL Server 2016+ (compat level 130+).
            migrationBuilder.Sql(@"
                INSERT INTO GlobalIngredientAliases (Id, GlobalIngredientId, Alias, NormalizedAlias)
                SELECT
                    NEWID(),
                    g.Id,
                    LTRIM(RTRIM(s.value)),
                    LOWER(LTRIM(RTRIM(s.value)))
                FROM GlobalIngredients g
                CROSS APPLY STRING_SPLIT(g.Aliases, ',') s
                WHERE g.Aliases IS NOT NULL
                  AND LEN(LTRIM(RTRIM(s.value))) > 0;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GlobalIngredientAliases");
        }
    }
}
