using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class FinalizeModelFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Columns already exist in the database (added manually or via previous migrations):
            // - QuotaType in MarksMaster
            // - Rank in MarksMaster
            // - ResultRemark in MarksMaster
            // - SGPI in MarksMaster
            /*
            migrationBuilder.AddColumn<string>(
                name: "QuotaType",
                table: "MarksMaster",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Rank",
                table: "MarksMaster",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultRemark",
                table: "MarksMaster",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SGPI",
                table: "MarksMaster",
                type: "decimal(18,2)",
                nullable: true);
            */

            migrationBuilder.AlterColumn<string>(
                name: "Semester",
                table: "ExamMaster",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            /*
            migrationBuilder.DropColumn(
                name: "QuotaType",
                table: "MarksMaster");

            migrationBuilder.DropColumn(
                name: "Rank",
                table: "MarksMaster");

            migrationBuilder.DropColumn(
                name: "ResultRemark",
                table: "MarksMaster");

            migrationBuilder.DropColumn(
                name: "SGPI",
                table: "MarksMaster");
            */

            migrationBuilder.AlterColumn<string>(
                name: "Semester",
                table: "ExamMaster",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);
        }
    }
}
