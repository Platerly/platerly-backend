using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platerly.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAllergenMonitoringIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AllergenMonitoringSubscriptions_WebhookSubscriptions_WebhookSubscriptionId",
                table: "AllergenMonitoringSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_AllergenMonitoringSubscriptions_WebhookSubscriptionId",
                table: "AllergenMonitoringSubscriptions");

            migrationBuilder.CreateIndex(
                name: "IX_AllergenMonitoringSubscriptions_ApiKeyId_IsActive",
                table: "AllergenMonitoringSubscriptions",
                columns: new[] { "ApiKeyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AllergenMonitoringSubscriptions_WebhookSubscriptionId",
                table: "AllergenMonitoringSubscriptions",
                column: "WebhookSubscriptionId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AllergenMonitoringSubscriptions_WebhookSubscriptions_WebhookSubscriptionId",
                table: "AllergenMonitoringSubscriptions",
                column: "WebhookSubscriptionId",
                principalTable: "WebhookSubscriptions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AllergenMonitoringSubscriptions_WebhookSubscriptions_WebhookSubscriptionId",
                table: "AllergenMonitoringSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_AllergenMonitoringSubscriptions_ApiKeyId_IsActive",
                table: "AllergenMonitoringSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_AllergenMonitoringSubscriptions_WebhookSubscriptionId",
                table: "AllergenMonitoringSubscriptions");

            migrationBuilder.CreateIndex(
                name: "IX_AllergenMonitoringSubscriptions_WebhookSubscriptionId",
                table: "AllergenMonitoringSubscriptions",
                column: "WebhookSubscriptionId");

            migrationBuilder.AddForeignKey(
                name: "FK_AllergenMonitoringSubscriptions_WebhookSubscriptions_WebhookSubscriptionId",
                table: "AllergenMonitoringSubscriptions",
                column: "WebhookSubscriptionId",
                principalTable: "WebhookSubscriptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
