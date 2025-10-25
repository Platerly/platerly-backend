using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Platerly.Api.Migrations
{
    public partial class RecipeTagsUniqueIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF NOT EXISTS (
  SELECT 1
  FROM sys.indexes
  WHERE name = 'IX_RecipeTags_RecipeId_TagId_UQ'
    AND object_id = OBJECT_ID('dbo.RecipeTags')
)
BEGIN
  CREATE UNIQUE INDEX IX_RecipeTags_RecipeId_TagId_UQ ON dbo.RecipeTags (RecipeId, TagId);
END
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (
  SELECT 1
  FROM sys.indexes
  WHERE name = 'IX_RecipeTags_RecipeId_TagId_UQ'
    AND object_id = OBJECT_ID('dbo.RecipeTags')
)
BEGIN
  DROP INDEX IX_RecipeTags_RecipeId_TagId_UQ ON dbo.RecipeTags;
END
");
        }
    }
}
