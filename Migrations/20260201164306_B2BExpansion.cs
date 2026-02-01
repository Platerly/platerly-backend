using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platerly.Api.Migrations
{
    /// <inheritdoc />
    public partial class B2BExpansion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IngredientSustainabilityData",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GlobalIngredientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CarbonScorePer100g = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    WaterUsagePer100g = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SeasonMonths = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TransportScore = table.Column<int>(type: "int", nullable: false),
                    OverallScore = table.Column<int>(type: "int", nullable: false),
                    DataSource = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngredientSustainabilityData", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IngredientSustainabilityData_GlobalIngredients_GlobalIngredientId",
                        column: x => x.GlobalIngredientId,
                        principalTable: "GlobalIngredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NutritionCaches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GlobalIngredientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UsdaFdcId = table.Column<int>(type: "int", nullable: true),
                    UsdaDescription = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CaloriesPer100g = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ProteinPer100g = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CarbsPer100g = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FatPer100g = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FiberPer100g = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SodiumMgPer100g = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SugarPer100g = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SaturatedFatPer100g = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CholesterolMgPer100g = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MatchConfidence = table.Column<int>(type: "int", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NutritionCaches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NutritionCaches_GlobalIngredients_GlobalIngredientId",
                        column: x => x.GlobalIngredientId,
                        principalTable: "GlobalIngredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebhookSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApiKeyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    Secret = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventTypes = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastTriggeredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookSubscriptions_ApiKeys_ApiKeyId",
                        column: x => x.ApiKeyId,
                        principalTable: "ApiKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WidgetConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApiKeyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ThemeJson = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    MaxRecipes = table.Column<int>(type: "int", nullable: false),
                    DietaryFilters = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RegionFilter = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LogoUrl = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WidgetConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WidgetConfigs_ApiKeys_ApiKeyId",
                        column: x => x.ApiKeyId,
                        principalTable: "ApiKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebhookEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextRetryAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookEvents_WebhookSubscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "WebhookSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IngredientSustainabilityData_GlobalIngredientId",
                table: "IngredientSustainabilityData",
                column: "GlobalIngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_NutritionCaches_GlobalIngredientId",
                table: "NutritionCaches",
                column: "GlobalIngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEvents_SubscriptionId",
                table: "WebhookEvents",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookSubscriptions_ApiKeyId",
                table: "WebhookSubscriptions",
                column: "ApiKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_WidgetConfigs_ApiKeyId",
                table: "WidgetConfigs",
                column: "ApiKeyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IngredientSustainabilityData");

            migrationBuilder.DropTable(
                name: "NutritionCaches");

            migrationBuilder.DropTable(
                name: "WebhookEvents");

            migrationBuilder.DropTable(
                name: "WidgetConfigs");

            migrationBuilder.DropTable(
                name: "WebhookSubscriptions");
        }
    }
}
