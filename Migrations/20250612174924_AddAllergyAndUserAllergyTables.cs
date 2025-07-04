using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platerly.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAllergyAndUserAllergyTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserAllergy_Allergy_AllergyId",
                table: "UserAllergy");

            migrationBuilder.DropForeignKey(
                name: "FK_UserAllergy_Users_UserId",
                table: "UserAllergy");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserAllergy",
                table: "UserAllergy");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Allergy",
                table: "Allergy");

            migrationBuilder.RenameTable(
                name: "UserAllergy",
                newName: "UserAllergies");

            migrationBuilder.RenameTable(
                name: "Allergy",
                newName: "Allergies");

            migrationBuilder.RenameIndex(
                name: "IX_UserAllergy_AllergyId",
                table: "UserAllergies",
                newName: "IX_UserAllergies_AllergyId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserAllergies",
                table: "UserAllergies",
                columns: new[] { "UserId", "AllergyId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Allergies",
                table: "Allergies",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_UserAllergies_Allergies_AllergyId",
                table: "UserAllergies",
                column: "AllergyId",
                principalTable: "Allergies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserAllergies_Users_UserId",
                table: "UserAllergies",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserAllergies_Allergies_AllergyId",
                table: "UserAllergies");

            migrationBuilder.DropForeignKey(
                name: "FK_UserAllergies_Users_UserId",
                table: "UserAllergies");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserAllergies",
                table: "UserAllergies");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Allergies",
                table: "Allergies");

            migrationBuilder.RenameTable(
                name: "UserAllergies",
                newName: "UserAllergy");

            migrationBuilder.RenameTable(
                name: "Allergies",
                newName: "Allergy");

            migrationBuilder.RenameIndex(
                name: "IX_UserAllergies_AllergyId",
                table: "UserAllergy",
                newName: "IX_UserAllergy_AllergyId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserAllergy",
                table: "UserAllergy",
                columns: new[] { "UserId", "AllergyId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Allergy",
                table: "Allergy",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_UserAllergy_Allergy_AllergyId",
                table: "UserAllergy",
                column: "AllergyId",
                principalTable: "Allergy",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserAllergy_Users_UserId",
                table: "UserAllergy",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
