using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platerly.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeBillingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BillingEmail",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PremiumCancelAtPeriodEnd",
                table: "Users",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PremiumCurrentPeriodEnd",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BillingEmail",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PremiumCancelAtPeriodEnd",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PremiumCurrentPeriodEnd",
                table: "Users");
        }
    }
}
