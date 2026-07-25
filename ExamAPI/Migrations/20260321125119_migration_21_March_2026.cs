using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class migration_21_March_2026 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.AlterColumn<Guid>(
                name: "AYID",
                table: "StudentEligibility",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

            // Columns already exist in the database (added manually or via hotfixes):
            // - Pattern in MarksMaster
            // - IsActive in ExamMaster
            // - RevaluationForExamId in ExamMaster
            /*
            migrationBuilder.AddColumn<string>(
                name: "Pattern",
                table: "MarksMaster",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "ExamMaster",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RevaluationForExamId",
                table: "ExamMaster",
                type: "uniqueidentifier",
                nullable: true);
            */
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            /*
            migrationBuilder.DropColumn(
                name: "Pattern",
                table: "MarksMaster");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "ExamMaster");

            migrationBuilder.DropColumn(
                name: "RevaluationForExamId",
                table: "ExamMaster");
            */

            migrationBuilder.AlterColumn<string>(
                name: "AYID",
                table: "StudentEligibility",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldMaxLength: 20,
                oldNullable: true);

        }
    }
}
