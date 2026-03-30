using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platerly.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWebIndexerInfra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BaseUrl",
                table: "RecipeIndexSources",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RobotsAllowed",
                table: "RecipeIndexSources",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RobotsCheckedAt",
                table: "RecipeIndexSources",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RobotsDisallowNote",
                table: "RecipeIndexSources",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ImageDisplayBlocked",
                table: "RecipeIndex",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SubmissionSource",
                table: "RecipeIndex",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BlockedDomains",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Domain = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    BlockedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    Reason = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ContactEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockedDomains", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlockedDomains_Domain",
                table: "BlockedDomains",
                column: "Domain",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlockedDomains");

            migrationBuilder.DropColumn(
                name: "BaseUrl",
                table: "RecipeIndexSources");

            migrationBuilder.DropColumn(
                name: "RobotsAllowed",
                table: "RecipeIndexSources");

            migrationBuilder.DropColumn(
                name: "RobotsCheckedAt",
                table: "RecipeIndexSources");

            migrationBuilder.DropColumn(
                name: "RobotsDisallowNote",
                table: "RecipeIndexSources");

            migrationBuilder.DropColumn(
                name: "ImageDisplayBlocked",
                table: "RecipeIndex");

            migrationBuilder.DropColumn(
                name: "SubmissionSource",
                table: "RecipeIndex");
        }
    }
}
