using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platerly.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTrendingEnhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "Users",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Region",
                table: "Users",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CommentCount",
                table: "RecipeTrendingCache",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LikeCount",
                table: "RecipeTrendingCache",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PreviousRank",
                table: "RecipeTrendingCache",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RankDelta",
                table: "RecipeTrendingCache",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RemixCount",
                table: "RecipeTrendingCache",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RepostCount",
                table: "RecipeTrendingCache",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SaveCount",
                table: "RecipeTrendingCache",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "VelocityScore",
                table: "RecipeTrendingCache",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "ViewCount",
                table: "RecipeTrendingCache",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "RecipeTrendingHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecipeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Score = table.Column<double>(type: "float", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeTrendingHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecipeTrendingHistory_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeViews",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecipeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IpHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Referrer = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ViewedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeViews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecipeViews_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecipeViews_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserTrendingCache",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Score = table.Column<double>(type: "float", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    PreviousRank = table.Column<int>(type: "int", nullable: true),
                    RankDelta = table.Column<int>(type: "int", nullable: false),
                    FollowerGrowth = table.Column<int>(type: "int", nullable: false),
                    RecipeEngagementTotal = table.Column<int>(type: "int", nullable: false),
                    TrendingRecipeCount = table.Column<int>(type: "int", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTrendingCache", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserTrendingCache_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeTrendingCache_VelocityScore",
                table: "RecipeTrendingCache",
                column: "VelocityScore");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeTrendingHistory_ComputedAt",
                table: "RecipeTrendingHistory",
                column: "ComputedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeTrendingHistory_RecipeId_ComputedAt",
                table: "RecipeTrendingHistory",
                columns: new[] { "RecipeId", "ComputedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeViews_RecipeId_IpHash_ViewedAt",
                table: "RecipeViews",
                columns: new[] { "RecipeId", "IpHash", "ViewedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeViews_RecipeId_ViewedAt",
                table: "RecipeViews",
                columns: new[] { "RecipeId", "ViewedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeViews_UserId",
                table: "RecipeViews",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeViews_ViewedAt",
                table: "RecipeViews",
                column: "ViewedAt");

            migrationBuilder.CreateIndex(
                name: "IX_UserTrendingCache_Rank",
                table: "UserTrendingCache",
                column: "Rank");

            migrationBuilder.CreateIndex(
                name: "IX_UserTrendingCache_Score",
                table: "UserTrendingCache",
                column: "Score");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecipeTrendingHistory");

            migrationBuilder.DropTable(
                name: "RecipeViews");

            migrationBuilder.DropTable(
                name: "UserTrendingCache");

            migrationBuilder.DropIndex(
                name: "IX_RecipeTrendingCache_VelocityScore",
                table: "RecipeTrendingCache");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Region",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CommentCount",
                table: "RecipeTrendingCache");

            migrationBuilder.DropColumn(
                name: "LikeCount",
                table: "RecipeTrendingCache");

            migrationBuilder.DropColumn(
                name: "PreviousRank",
                table: "RecipeTrendingCache");

            migrationBuilder.DropColumn(
                name: "RankDelta",
                table: "RecipeTrendingCache");

            migrationBuilder.DropColumn(
                name: "RemixCount",
                table: "RecipeTrendingCache");

            migrationBuilder.DropColumn(
                name: "RepostCount",
                table: "RecipeTrendingCache");

            migrationBuilder.DropColumn(
                name: "SaveCount",
                table: "RecipeTrendingCache");

            migrationBuilder.DropColumn(
                name: "VelocityScore",
                table: "RecipeTrendingCache");

            migrationBuilder.DropColumn(
                name: "ViewCount",
                table: "RecipeTrendingCache");
        }
    }
}
