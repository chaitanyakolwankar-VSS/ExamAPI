using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddRawMarksAndExamType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RawMarks",
                table: "StudentMarks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExamType",
                table: "RuleSet",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RawMarks",
                table: "StudentMarks");

            migrationBuilder.DropColumn(
                name: "ExamType",
                table: "RuleSet");
        }
    }
}
