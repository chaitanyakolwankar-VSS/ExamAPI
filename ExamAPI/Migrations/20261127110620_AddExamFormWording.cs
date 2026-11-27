using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddExamFormWording : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeclaredAt",
                table: "ExamApplication",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExamFormDeclaration",
                table: "CollegeSettings",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExamFormNote",
                table: "CollegeSettings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeclaredAt",
                table: "ExamApplication");

            migrationBuilder.DropColumn(
                name: "ExamFormDeclaration",
                table: "CollegeSettings");

            migrationBuilder.DropColumn(
                name: "ExamFormNote",
                table: "CollegeSettings");
        }
    }
}
