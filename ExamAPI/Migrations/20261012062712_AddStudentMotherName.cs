using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentMotherName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MotherName",
                table: "StudentMaster",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MotherName",
                table: "StudentMaster");
        }
    }
}
