using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platerly.Api.Migrations
{
    /// <inheritdoc />
    public partial class FixPendingModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AllergenHitDto",
                columns: table => new
                {
                    IngredientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IngredientLabel = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Quantity = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GlobalIngredientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AllergenType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HitSource = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HitSeverity = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HitSeverityLevel = table.Column<int>(type: "int", nullable: true),
                    MayContain = table.Column<bool>(type: "bit", nullable: false),
                    CrossContactRisk = table.Column<bool>(type: "bit", nullable: false),
                    ProcessingAid = table.Column<bool>(type: "bit", nullable: false),
                    LegacyIsAllergenicFlag = table.Column<bool>(type: "bit", nullable: false),
                    GlobalIsAllergenicFlag = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllergenHitDto");
        }
    }
}
