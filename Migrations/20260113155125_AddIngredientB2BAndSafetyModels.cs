using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platerly.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIngredientB2BAndSafetyModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "GlobalIngredients",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "Aliases",
                table: "GlobalIngredients",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AverageRecipeRating",
                table: "GlobalIngredients",
                type: "decimal(3,2)",
                precision: 3,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "GlobalIngredients",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "GlobalIngredients",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<string>(
                name: "DietaryFlags",
                table: "GlobalIngredients",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "GlobalIngredients",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsVerified",
                table: "GlobalIngredients",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUsedAt",
                table: "GlobalIngredients",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MergedIntoId",
                table: "GlobalIngredients",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "GlobalIngredients",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NutritionalCategory",
                table: "GlobalIngredients",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PopularityRank",
                table: "GlobalIngredients",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryRegion",
                table: "GlobalIngredients",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TotalUsageCount",
                table: "GlobalIngredients",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "UniqueRecipeCount",
                table: "GlobalIngredients",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "GlobalIngredients",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DietaryTrends",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DietaryTag = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Region = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RecipeCount = table.Column<int>(type: "int", nullable: false),
                    UniqueUsers = table.Column<int>(type: "int", nullable: false),
                    GrowthRate = table.Column<decimal>(type: "decimal(8,4)", precision: 8, scale: 4, nullable: true),
                    MarketShare = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DietaryTrends", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IngredientCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ParentCategoryId = table.Column<int>(type: "int", nullable: true),
                    IconName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CommonAllergens = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DietaryFlags = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngredientCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IngredientCategories_IngredientCategories_ParentCategoryId",
                        column: x => x.ParentCategoryId,
                        principalTable: "IngredientCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IngredientPairings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IngredientAId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IngredientBId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CoOccurrenceCount = table.Column<int>(type: "int", nullable: false),
                    AffinityScore = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    TopRegion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CommonCuisines = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngredientPairings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IngredientPairings_GlobalIngredients_IngredientAId",
                        column: x => x.IngredientAId,
                        principalTable: "GlobalIngredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IngredientPairings_GlobalIngredients_IngredientBId",
                        column: x => x.IngredientBId,
                        principalTable: "GlobalIngredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IngredientSearchLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Query = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    MatchedIngredientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WasFound = table.Column<bool>(type: "bit", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Region = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SearchContext = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngredientSearchLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IngredientSubstitutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalIngredientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubstituteIngredientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    AllergyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConversionRatio = table.Column<decimal>(type: "decimal(6,3)", precision: 6, scale: 3, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ConfidenceScore = table.Column<int>(type: "int", nullable: false),
                    PositiveVotes = table.Column<int>(type: "int", nullable: false),
                    NegativeVotes = table.Column<int>(type: "int", nullable: false),
                    UsageCount = table.Column<int>(type: "int", nullable: false),
                    SuggestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsVerified = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngredientSubstitutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IngredientSubstitutions_Allergies_AllergyId",
                        column: x => x.AllergyId,
                        principalTable: "Allergies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_IngredientSubstitutions_GlobalIngredients_OriginalIngredientId",
                        column: x => x.OriginalIngredientId,
                        principalTable: "GlobalIngredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IngredientSubstitutions_GlobalIngredients_SubstituteIngredientId",
                        column: x => x.SubstituteIngredientId,
                        principalTable: "GlobalIngredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IngredientSubstitutions_Users_SuggestedByUserId",
                        column: x => x.SuggestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "IngredientTrendDaily",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GlobalIngredientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Region = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UsageCount = table.Column<int>(type: "int", nullable: false),
                    UniqueRecipes = table.Column<int>(type: "int", nullable: false),
                    UniqueUsers = table.Column<int>(type: "int", nullable: false),
                    DayOverDayChange = table.Column<decimal>(type: "decimal(8,4)", precision: 8, scale: 4, nullable: true),
                    WeekOverWeekChange = table.Column<decimal>(type: "decimal(8,4)", precision: 8, scale: 4, nullable: true),
                    TrendDirection = table.Column<int>(type: "int", nullable: false),
                    PopularityRank = table.Column<int>(type: "int", nullable: true),
                    ComputedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngredientTrendDaily", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IngredientTrendDaily_GlobalIngredients_GlobalIngredientId",
                        column: x => x.GlobalIngredientId,
                        principalTable: "GlobalIngredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IngredientUsageLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GlobalIngredientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Region = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CountryCode = table.Column<string>(type: "varchar(2)", unicode: false, maxLength: 2, nullable: true),
                    City = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Quantity = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    QuantityNumeric = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    SubstitutedForIngredientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DietaryTags = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngredientUsageLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IngredientUsageLogs_GlobalIngredients_GlobalIngredientId",
                        column: x => x.GlobalIngredientId,
                        principalTable: "GlobalIngredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IngredientUsageLogs_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProhibitedIngredients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Keyword = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Category = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    MatchType = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    AdminNotes = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Severity = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProhibitedIngredients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RegionalIngredientStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GlobalIngredientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Region = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TotalUsageCount = table.Column<long>(type: "bigint", nullable: false),
                    PenetrationRate = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    AverageQuantity = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: true),
                    CommonUnit = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    PopularityRank = table.Column<int>(type: "int", nullable: false),
                    SeasonalPeakMonths = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CommonPairings = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CommonDietaryContexts = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegionalIngredientStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegionalIngredientStats_GlobalIngredients_GlobalIngredientId",
                        column: x => x.GlobalIngredientId,
                        principalTable: "GlobalIngredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubstitutionVotes",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubstitutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsPositive = table.Column<bool>(type: "bit", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubstitutionVotes", x => new { x.UserId, x.SubstitutionId });
                    table.ForeignKey(
                        name: "FK_SubstitutionVotes_IngredientSubstitutions_SubstitutionId",
                        column: x => x.SubstitutionId,
                        principalTable: "IngredientSubstitutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SubstitutionVotes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GlobalIngredients_CategoryId",
                table: "GlobalIngredients",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_GlobalIngredients_Name",
                table: "GlobalIngredients",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_GlobalIngredients_NormalizedName",
                table: "GlobalIngredients",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_DietaryTrends_DietaryTag_Date",
                table: "DietaryTrends",
                columns: new[] { "DietaryTag", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_IngredientCategories_Name",
                table: "IngredientCategories",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientCategories_ParentCategoryId",
                table: "IngredientCategories",
                column: "ParentCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientCategories_Slug",
                table: "IngredientCategories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IngredientPairings_IngredientAId_IngredientBId",
                table: "IngredientPairings",
                columns: new[] { "IngredientAId", "IngredientBId" });

            migrationBuilder.CreateIndex(
                name: "IX_IngredientPairings_IngredientBId",
                table: "IngredientPairings",
                column: "IngredientBId");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientSearchLogs_Query_CreatedAt",
                table: "IngredientSearchLogs",
                columns: new[] { "Query", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IngredientSubstitutions_AllergyId",
                table: "IngredientSubstitutions",
                column: "AllergyId");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientSubstitutions_ConfidenceScore",
                table: "IngredientSubstitutions",
                column: "ConfidenceScore");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientSubstitutions_OriginalIngredientId_SubstituteIngredientId",
                table: "IngredientSubstitutions",
                columns: new[] { "OriginalIngredientId", "SubstituteIngredientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IngredientSubstitutions_Reason",
                table: "IngredientSubstitutions",
                column: "Reason");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientSubstitutions_SubstituteIngredientId",
                table: "IngredientSubstitutions",
                column: "SubstituteIngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientSubstitutions_SuggestedByUserId",
                table: "IngredientSubstitutions",
                column: "SuggestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientTrendDaily_Date",
                table: "IngredientTrendDaily",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientTrendDaily_GlobalIngredientId_Date",
                table: "IngredientTrendDaily",
                columns: new[] { "GlobalIngredientId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_IngredientTrendDaily_Region_Date",
                table: "IngredientTrendDaily",
                columns: new[] { "Region", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_IngredientUsageLogs_CreatedAt",
                table: "IngredientUsageLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientUsageLogs_GlobalIngredientId_CreatedAt",
                table: "IngredientUsageLogs",
                columns: new[] { "GlobalIngredientId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IngredientUsageLogs_RecipeId",
                table: "IngredientUsageLogs",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientUsageLogs_Region_CreatedAt",
                table: "IngredientUsageLogs",
                columns: new[] { "Region", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProhibitedIngredients_Category",
                table: "ProhibitedIngredients",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_ProhibitedIngredients_IsActive",
                table: "ProhibitedIngredients",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ProhibitedIngredients_Keyword",
                table: "ProhibitedIngredients",
                column: "Keyword");

            migrationBuilder.CreateIndex(
                name: "IX_RegionalIngredientStats_GlobalIngredientId",
                table: "RegionalIngredientStats",
                column: "GlobalIngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_RegionalIngredientStats_Region_GlobalIngredientId",
                table: "RegionalIngredientStats",
                columns: new[] { "Region", "GlobalIngredientId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubstitutionVotes_SubstitutionId",
                table: "SubstitutionVotes",
                column: "SubstitutionId");

            migrationBuilder.AddForeignKey(
                name: "FK_GlobalIngredients_IngredientCategories_CategoryId",
                table: "GlobalIngredients",
                column: "CategoryId",
                principalTable: "IngredientCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GlobalIngredients_IngredientCategories_CategoryId",
                table: "GlobalIngredients");

            migrationBuilder.DropTable(
                name: "DietaryTrends");

            migrationBuilder.DropTable(
                name: "IngredientCategories");

            migrationBuilder.DropTable(
                name: "IngredientPairings");

            migrationBuilder.DropTable(
                name: "IngredientSearchLogs");

            migrationBuilder.DropTable(
                name: "IngredientTrendDaily");

            migrationBuilder.DropTable(
                name: "IngredientUsageLogs");

            migrationBuilder.DropTable(
                name: "ProhibitedIngredients");

            migrationBuilder.DropTable(
                name: "RegionalIngredientStats");

            migrationBuilder.DropTable(
                name: "SubstitutionVotes");

            migrationBuilder.DropTable(
                name: "IngredientSubstitutions");

            migrationBuilder.DropIndex(
                name: "IX_GlobalIngredients_CategoryId",
                table: "GlobalIngredients");

            migrationBuilder.DropIndex(
                name: "IX_GlobalIngredients_Name",
                table: "GlobalIngredients");

            migrationBuilder.DropIndex(
                name: "IX_GlobalIngredients_NormalizedName",
                table: "GlobalIngredients");

            migrationBuilder.DropColumn(
                name: "Aliases",
                table: "GlobalIngredients");

            migrationBuilder.DropColumn(
                name: "AverageRecipeRating",
                table: "GlobalIngredients");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "GlobalIngredients");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "GlobalIngredients");

            migrationBuilder.DropColumn(
                name: "DietaryFlags",
                table: "GlobalIngredients");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "GlobalIngredients");

            migrationBuilder.DropColumn(
                name: "IsVerified",
                table: "GlobalIngredients");

            migrationBuilder.DropColumn(
                name: "LastUsedAt",
                table: "GlobalIngredients");

            migrationBuilder.DropColumn(
                name: "MergedIntoId",
                table: "GlobalIngredients");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "GlobalIngredients");

            migrationBuilder.DropColumn(
                name: "NutritionalCategory",
                table: "GlobalIngredients");

            migrationBuilder.DropColumn(
                name: "PopularityRank",
                table: "GlobalIngredients");

            migrationBuilder.DropColumn(
                name: "PrimaryRegion",
                table: "GlobalIngredients");

            migrationBuilder.DropColumn(
                name: "TotalUsageCount",
                table: "GlobalIngredients");

            migrationBuilder.DropColumn(
                name: "UniqueRecipeCount",
                table: "GlobalIngredients");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "GlobalIngredients");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "GlobalIngredients",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);
        }
    }
}
