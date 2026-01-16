using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platerly.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCookbookIdToNotification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CookbookId",
                table: "Notifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_CookbookId",
                table: "Notifications",
                column: "CookbookId");

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Cookbooks_CookbookId",
                table: "Notifications",
                column: "CookbookId",
                principalTable: "Cookbooks",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Cookbooks_CookbookId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_CookbookId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "CookbookId",
                table: "Notifications");
        }
    }
}
