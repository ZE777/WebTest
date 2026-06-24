using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibrationDashboard.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMachineType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Machine",
                type: "nvarchar(40)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Type",
                table: "Machine");
        }
    }
}
