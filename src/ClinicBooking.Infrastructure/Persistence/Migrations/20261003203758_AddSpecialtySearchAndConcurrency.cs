using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicBooking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpecialtySearchAndConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Specialties_NameAr",
                table: "Specialties");

            migrationBuilder.DropIndex(
                name: "UX_Specialties_NameEn",
                table: "Specialties");

            migrationBuilder.AddColumn<string>(
                name: "NameArNormalized",
                table: "Specialties",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NameEnNormalized",
                table: "Specialties",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Specialties",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "UX_Specialties_NameArNormalized",
                table: "Specialties",
                column: "NameArNormalized",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Specialties_NameEnNormalized",
                table: "Specialties",
                column: "NameEnNormalized",
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Specialties_NameArNormalized",
                table: "Specialties");

            migrationBuilder.DropIndex(
                name: "UX_Specialties_NameEnNormalized",
                table: "Specialties");

            migrationBuilder.DropColumn(
                name: "NameArNormalized",
                table: "Specialties");

            migrationBuilder.DropColumn(
                name: "NameEnNormalized",
                table: "Specialties");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Specialties");

            migrationBuilder.CreateIndex(
                name: "UX_Specialties_NameAr",
                table: "Specialties",
                column: "NameAr",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Specialties_NameEn",
                table: "Specialties",
                column: "NameEn",
                unique: true,
                filter: "[IsDeleted] = 0");
        }
    }
}
