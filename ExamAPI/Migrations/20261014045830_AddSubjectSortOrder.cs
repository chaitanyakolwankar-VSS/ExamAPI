using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddSubjectSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "SubjectMaster",
                type: "int",
                nullable: true);

            // Owner decision 2026-10-14: subjects have a staff-set order. Existing subjects start in the order
            // they were created (then by code) within their branch / pattern / semester, so every list keeps
            // looking sensible until staff reorder them. Soft-deleted rows stay NULL.
            // EXEC defers compiling the UPDATE until the column exists: the idempotent script (Build-Package) puts
            // the ALTER and the UPDATE in one batch, which would otherwise fail with "Invalid column name".
            migrationBuilder.Sql(@"EXEC(N'
UPDATE s SET s.SortOrder = n.rn
FROM SubjectMaster s
JOIN (
    SELECT SubjectId,
           ROW_NUMBER() OVER (PARTITION BY CollegeId, CourseId, Pattern, SemId
                              ORDER BY CreatedAt, SubjectCode) AS rn
    FROM SubjectMaster
    WHERE IsDeleted = 0
) n ON n.SubjectId = s.SubjectId
WHERE s.SortOrder IS NULL;')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "SubjectMaster");
        }
    }
}
