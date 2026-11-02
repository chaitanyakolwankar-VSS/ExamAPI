using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddHallTicketInstructions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HallTicketInstructions",
                table: "CollegeSettings",
                type: "nvarchar(1500)",
                maxLength: 1500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HallTicketInstructionsOff",
                table: "CollegeSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HallTicketInstructions",
                table: "CollegeSettings");

            migrationBuilder.DropColumn(
                name: "HallTicketInstructionsOff",
                table: "CollegeSettings");
        }
    }
}
