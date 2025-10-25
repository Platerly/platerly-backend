using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platerly.Api.Migrations
{
    /// <inheritdoc />
    public partial class TagNameUniqueLowerIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Add persisted computed lowercased column for case-insensitive uniqueness
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.Tags', 'NormalizedName') IS NULL
BEGIN
    ALTER TABLE dbo.Tags ADD NormalizedName AS LOWER([Name]) PERSISTED;
END
");

            // 2) Defensive trim
            migrationBuilder.Sql(@"
UPDATE dbo.Tags SET [Name] = LTRIM(RTRIM([Name])) WHERE [Name] IS NOT NULL;
");

            // 3) DEDUPE using a temp table (works across multiple statements)
            migrationBuilder.Sql(@"
IF OBJECT_ID('tempdb..#TagKeep') IS NOT NULL DROP TABLE #TagKeep;

SELECT NormalizedName, MIN(Id) AS KeepId
INTO #TagKeep
FROM dbo.Tags
GROUP BY NormalizedName;

-- Point RecipeTags to the kept TagId
UPDATE rt
SET rt.TagId = k.KeepId
FROM dbo.RecipeTags rt
JOIN dbo.Tags t ON rt.TagId = t.Id
JOIN #TagKeep k ON t.NormalizedName = k.NormalizedName
WHERE rt.TagId <> k.KeepId;

-- Remove duplicate Tag rows (keep only the KeepId per NormalizedName)
DELETE t
FROM dbo.Tags t
JOIN #TagKeep k ON t.NormalizedName = k.NormalizedName
WHERE t.Id <> k.KeepId;

-- Clean up temp table (optional; will auto-drop at end of session)
DROP TABLE #TagKeep;
");

            // 4) Unique index on NormalizedName
            migrationBuilder.Sql(@"
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_Tags_NormalizedName_UQ'
      AND object_id = OBJECT_ID('dbo.Tags')
)
BEGIN
    CREATE UNIQUE INDEX IX_Tags_NormalizedName_UQ ON dbo.Tags (NormalizedName);
END
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop unique index if exists
            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_Tags_NormalizedName_UQ'
      AND object_id = OBJECT_ID('dbo.Tags')
)
BEGIN
    DROP INDEX IX_Tags_NormalizedName_UQ ON dbo.Tags;
END
");

            // Drop computed column if exists
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.Tags', 'NormalizedName') IS NOT NULL
BEGIN
    ALTER TABLE dbo.Tags DROP COLUMN NormalizedName;
END
");
        }
    }
}
