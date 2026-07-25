using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddDyslexiaStudentToStudentMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Column was added directly via SQL hotfix.
            // This migration records it in EF migration history.
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM sys.columns
                    WHERE object_id = OBJECT_ID('StudentMaster')
                    AND name = 'DyslexiaStudent'
                )
                BEGIN
                    ALTER TABLE [StudentMaster] ADD [DyslexiaStudent] bit NOT NULL DEFAULT 0
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DyslexiaStudent",
                table: "StudentMaster");
        }
    }
}
