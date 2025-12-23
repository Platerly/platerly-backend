using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platerly.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAllergenLexicon_FixNoNav : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AllergenKeywords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Text = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ENumber = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllergenKeywords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AllergenKeywordAllergies",
                columns: table => new
                {
                    AllergenKeywordId = table.Column<int>(type: "int", nullable: false),
                    AllergyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllergenKeywordAllergies", x => new { x.AllergenKeywordId, x.AllergyId });
                    table.ForeignKey(
                        name: "FK_AllergenKeywordAllergies_AllergenKeywords_AllergenKeywordId",
                        column: x => x.AllergenKeywordId,
                        principalTable: "AllergenKeywords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AllergenKeywordAllergies_Allergies_AllergyId",
                        column: x => x.AllergyId,
                        principalTable: "Allergies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AllergenKeywordAllergies_AllergyId",
                table: "AllergenKeywordAllergies",
                column: "AllergyId");

            migrationBuilder.CreateIndex(
                name: "IX_AllergenKeywords_Text_Kind",
                table: "AllergenKeywords",
                columns: new[] { "Text", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllergenKeywordAllergies");

            migrationBuilder.DropTable(
                name: "AllergenKeywords");
        }
    }
}
