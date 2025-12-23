using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platerly.Api.Migrations
{
    public partial class AddAllergySeverityTraceFlags : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- UserAllergies: per-user prefs ---
            migrationBuilder.AddColumn<bool>(
                name: "IncludeTrace",
                table: "UserAllergies",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEnabled",
                table: "UserAllergies",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "MinSeverity",
                table: "UserAllergies",
                type: "varchar(8)",
                unicode: false,
                maxLength: 8,
                nullable: false,
                defaultValue: "Low");

            migrationBuilder.Sql("UPDATE [UserAllergies] SET [MinSeverity] = 'Low' WHERE [MinSeverity] IS NULL OR [MinSeverity] = ''");

            // --- IngredientAllergens: add override columns first ---
            migrationBuilder.AddColumn<bool>(
                name: "CrossContactRisk",
                table: "IngredientAllergens",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MayContain",
                table: "IngredientAllergens",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ProcessingAid",
                table: "IngredientAllergens",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Severity",
                table: "IngredientAllergens",
                type: "varchar(8)",
                unicode: false,
                maxLength: 8,
                nullable: true);

            // --- IngredientAllergens.AllergenType change: drop PK -> alter -> re-add PK
            migrationBuilder.DropPrimaryKey(
                name: "PK_IngredientAllergens",
                table: "IngredientAllergens");

            migrationBuilder.Sql(@"
                DECLARE @dc sysname;
                SELECT @dc = d.name
                FROM sys.default_constraints d
                JOIN sys.columns c ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id
                WHERE d.parent_object_id = OBJECT_ID(N'[IngredientAllergens]')
                  AND c.name = N'AllergenType';
                IF @dc IS NOT NULL EXEC(N'ALTER TABLE [IngredientAllergens] DROP CONSTRAINT [' + @dc + ']');
            ");

            migrationBuilder.AlterColumn<string>(
                name: "AllergenType",
                table: "IngredientAllergens",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddPrimaryKey(
                name: "PK_IngredientAllergens",
                table: "IngredientAllergens",
                columns: new[] { "IngredientId", "AllergenType" });

            // --- GlobalIngredientAllergens: add flags + metadata + severity
            migrationBuilder.AddColumn<bool>(
                name: "CrossContactRisk",
                table: "GlobalIngredientAllergens",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ENumber",
                table: "GlobalIngredientAllergens",
                type: "varchar(16)",
                unicode: false,
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MayContain",
                table: "GlobalIngredientAllergens",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "GlobalIngredientAllergens",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ProcessingAid",
                table: "GlobalIngredientAllergens",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Severity",
                table: "GlobalIngredientAllergens",
                type: "varchar(8)",
                unicode: false,
                maxLength: 8,
                nullable: false,
                defaultValue: "Low");

            // --- GlobalIngredientAllergens.AllergenType change:
            // drop UNIQUE INDEX + PK -> alter -> re-add PK + re-add UNIQUE INDEX

            // 1) Drop the unique index that depends on AllergenType
            // Try the known name first; if not present, the SQL fallback removes any matching index.
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_GlobalIngredientAllergens_GlobalIngredient_AllergenType' AND object_id = OBJECT_ID(N'[GlobalIngredientAllergens]'))
                    DROP INDEX [UX_GlobalIngredientAllergens_GlobalIngredient_AllergenType] ON [GlobalIngredientAllergens];
            ");

            // 2) Drop PK
            migrationBuilder.DropPrimaryKey(
                name: "PK_GlobalIngredientAllergens",
                table: "GlobalIngredientAllergens");

            // 3) Drop default constraint on column (if any)
            migrationBuilder.Sql(@"
                DECLARE @dc2 sysname;
                SELECT @dc2 = d.name
                FROM sys.default_constraints d
                JOIN sys.columns c ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id
                WHERE d.parent_object_id = OBJECT_ID(N'[GlobalIngredientAllergens]')
                  AND c.name = N'AllergenType';
                IF @dc2 IS NOT NULL EXEC(N'ALTER TABLE [GlobalIngredientAllergens] DROP CONSTRAINT [' + @dc2 + ']');
            ");

            // 4) Alter the column
            migrationBuilder.AlterColumn<string>(
                name: "AllergenType",
                table: "GlobalIngredientAllergens",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            // 5) Re-add PK
            migrationBuilder.AddPrimaryKey(
                name: "PK_GlobalIngredientAllergens",
                table: "GlobalIngredientAllergens",
                columns: new[] { "GlobalIngredientId", "AllergenType" });

            // 6) Re-create the unique index
            migrationBuilder.CreateIndex(
                name: "UX_GlobalIngredientAllergens_GlobalIngredient_AllergenType",
                table: "GlobalIngredientAllergens",
                columns: new[] { "GlobalIngredientId", "AllergenType" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse GlobalIngredientAllergens: drop index + PK -> alter -> re-add PK
            migrationBuilder.DropIndex(
                name: "UX_GlobalIngredientAllergens_GlobalIngredient_AllergenType",
                table: "GlobalIngredientAllergens");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GlobalIngredientAllergens",
                table: "GlobalIngredientAllergens");

            migrationBuilder.AlterColumn<string>(
                name: "AllergenType",
                table: "GlobalIngredientAllergens",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldUnicode: false,
                oldMaxLength: 64);

            migrationBuilder.AddPrimaryKey(
                name: "PK_GlobalIngredientAllergens",
                table: "GlobalIngredientAllergens",
                columns: new[] { "GlobalIngredientId", "AllergenType" });

            // Drop added columns (GlobalIngredientAllergens)
            migrationBuilder.DropColumn(name: "CrossContactRisk", table: "GlobalIngredientAllergens");
            migrationBuilder.DropColumn(name: "ENumber", table: "GlobalIngredientAllergens");
            migrationBuilder.DropColumn(name: "MayContain", table: "GlobalIngredientAllergens");
            migrationBuilder.DropColumn(name: "Notes", table: "GlobalIngredientAllergens");
            migrationBuilder.DropColumn(name: "ProcessingAid", table: "GlobalIngredientAllergens");
            migrationBuilder.DropColumn(name: "Severity", table: "GlobalIngredientAllergens");

            // Reverse IngredientAllergens PK change
            migrationBuilder.DropPrimaryKey(
                name: "PK_IngredientAllergens",
                table: "IngredientAllergens");

            migrationBuilder.AlterColumn<string>(
                name: "AllergenType",
                table: "IngredientAllergens",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldUnicode: false,
                oldMaxLength: 64);

            migrationBuilder.AddPrimaryKey(
                name: "PK_IngredientAllergens",
                table: "IngredientAllergens",
                columns: new[] { "IngredientId", "AllergenType" });

            // Drop added columns (IngredientAllergens)
            migrationBuilder.DropColumn(name: "CrossContactRisk", table: "IngredientAllergens");
            migrationBuilder.DropColumn(name: "MayContain", table: "IngredientAllergens");
            migrationBuilder.DropColumn(name: "ProcessingAid", table: "IngredientAllergens");
            migrationBuilder.DropColumn(name: "Severity", table: "IngredientAllergens");

            // Drop added columns (UserAllergies)
            migrationBuilder.DropColumn(name: "IncludeTrace", table: "UserAllergies");
            migrationBuilder.DropColumn(name: "IsEnabled", table: "UserAllergies");
            migrationBuilder.DropColumn(name: "MinSeverity", table: "UserAllergies");
        }
    }
}
