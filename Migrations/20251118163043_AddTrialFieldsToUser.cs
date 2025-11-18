using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platerly.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTrialFieldsToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TrialEndUtc",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TrialStartUtc",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TrialEndUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TrialStartUtc",
                table: "Users");
        }
    }
}
