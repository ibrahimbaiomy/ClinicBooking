using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicBooking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProtectedUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsProtected",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // The existing first user (the lowest id) becomes the protected administrator (D64), whatever its
            // state; a database without users gets the flag from the seeder instead.
            migrationBuilder.Sql(
                "UPDATE [AspNetUsers] SET [IsProtected] = 1 WHERE [Id] = (SELECT MIN([Id]) FROM [AspNetUsers]);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsProtected",
                table: "AspNetUsers");
        }
    }
}
