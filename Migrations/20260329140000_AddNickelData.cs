using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platerly.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNickelData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "NickelMicrogramsPerServing",
                table: "GlobalIngredients",
                type: "decimal(10,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NickelServingSizeGrams",
                table: "GlobalIngredients",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NickelClassification",
                table: "GlobalIngredients",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "NickelDataSource",
                table: "GlobalIngredients",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NickelDataSourceYear",
                table: "GlobalIngredients",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NickelDataPointCount",
                table: "GlobalIngredients",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NickelStandardDeviation",
                table: "GlobalIngredients",
                type: "decimal(10,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NickelConfidenceLevel",
                table: "GlobalIngredients",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "NickelHighVariability",
                table: "GlobalIngredients",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NickelPreparationNote",
                table: "GlobalIngredients",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "NickelMicrogramsPerServing", table: "GlobalIngredients");
            migrationBuilder.DropColumn(name: "NickelServingSizeGrams", table: "GlobalIngredients");
            migrationBuilder.DropColumn(name: "NickelClassification", table: "GlobalIngredients");
            migrationBuilder.DropColumn(name: "NickelDataSource", table: "GlobalIngredients");
            migrationBuilder.DropColumn(name: "NickelDataSourceYear", table: "GlobalIngredients");
            migrationBuilder.DropColumn(name: "NickelDataPointCount", table: "GlobalIngredients");
            migrationBuilder.DropColumn(name: "NickelStandardDeviation", table: "GlobalIngredients");
            migrationBuilder.DropColumn(name: "NickelConfidenceLevel", table: "GlobalIngredients");
            migrationBuilder.DropColumn(name: "NickelHighVariability", table: "GlobalIngredients");
            migrationBuilder.DropColumn(name: "NickelPreparationNote", table: "GlobalIngredients");
        }
    }
}
