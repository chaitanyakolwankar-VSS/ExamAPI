using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class DropLegacyColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ResolutionMaster becomes one integer limit per exam x head: drop rows without an exam,
            // turn blank/garbage/negative text into 0, and keep only the latest row per exam x head.
            migrationBuilder.Sql(@"
DELETE FROM ResolutionMaster WHERE ExamID IS NULL;
UPDATE ResolutionMaster SET Resolution = '0'
    WHERE TRY_CAST(LTRIM(RTRIM(Resolution)) AS int) IS NULL OR TRY_CAST(LTRIM(RTRIM(Resolution)) AS int) < 0;
WITH ranked AS (
    SELECT ROW_NUMBER() OVER (PARTITION BY ExamID, SubjectCreditID ORDER BY COALESCE(UpdatedAt, CreatedAt) DESC) AS rn
    FROM ResolutionMaster WHERE IsDeleted = 0)
DELETE FROM ranked WHERE rn > 1;");

            migrationBuilder.DropForeignKey(
                name: "FK_ResolutionMaster_AcademicYear_AYID",
                table: "ResolutionMaster");

            migrationBuilder.DropForeignKey(
                name: "FK_ResolutionMaster_CourseMaster_CourseID",
                table: "ResolutionMaster");

            migrationBuilder.DropForeignKey(
                name: "FK_ResolutionMaster_ExamMaster_ExamID",
                table: "ResolutionMaster");

            migrationBuilder.DropIndex(
                name: "IX_ResolutionMaster_AYID",
                table: "ResolutionMaster");

            migrationBuilder.DropIndex(
                name: "IX_ResolutionMaster_CourseID",
                table: "ResolutionMaster");

            migrationBuilder.DropIndex(
                name: "IX_ResolutionMaster_ExamID",
                table: "ResolutionMaster");

            migrationBuilder.DropColumn(
                name: "HeadFormula",
                table: "SubjectCredits");

            migrationBuilder.DropColumn(
                name: "HeadResolution",
                table: "SubjectCredits");

            migrationBuilder.DropColumn(
                name: "Grade",
                table: "StudentMarks");

            migrationBuilder.DropColumn(
                name: "GradePoint",
                table: "StudentMarks");

            migrationBuilder.DropColumn(
                name: "RawGradePoint",
                table: "StudentMarks");

            migrationBuilder.DropColumn(
                name: "Remark",
                table: "StudentMarks");

            migrationBuilder.DropColumn(
                name: "AYID",
                table: "ResolutionMaster");

            migrationBuilder.DropColumn(
                name: "CourseID",
                table: "ResolutionMaster");

            migrationBuilder.DropColumn(
                name: "Head",
                table: "ResolutionMaster");

            migrationBuilder.DropColumn(
                name: "Remark",
                table: "ResolutionMaster");

            migrationBuilder.AlterColumn<int>(
                name: "Resolution",
                table: "ResolutionMaster",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ExamID",
                table: "ResolutionMaster",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResolutionMaster_ExamID_SubjectCreditID",
                table: "ResolutionMaster",
                columns: new[] { "ExamID", "SubjectCreditID" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_ResolutionMaster_ExamMaster_ExamID",
                table: "ResolutionMaster",
                column: "ExamID",
                principalTable: "ExamMaster",
                principalColumn: "ExamId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ResolutionMaster_ExamMaster_ExamID",
                table: "ResolutionMaster");

            migrationBuilder.DropIndex(
                name: "IX_ResolutionMaster_ExamID_SubjectCreditID",
                table: "ResolutionMaster");

            migrationBuilder.AddColumn<string>(
                name: "HeadFormula",
                table: "SubjectCredits",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeadResolution",
                table: "SubjectCredits",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Grade",
                table: "StudentMarks",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GradePoint",
                table: "StudentMarks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RawGradePoint",
                table: "StudentMarks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Remark",
                table: "StudentMarks",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Resolution",
                table: "ResolutionMaster",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<Guid>(
                name: "ExamID",
                table: "ResolutionMaster",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "AYID",
                table: "ResolutionMaster",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CourseID",
                table: "ResolutionMaster",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Head",
                table: "ResolutionMaster",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Remark",
                table: "ResolutionMaster",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResolutionMaster_AYID",
                table: "ResolutionMaster",
                column: "AYID");

            migrationBuilder.CreateIndex(
                name: "IX_ResolutionMaster_CourseID",
                table: "ResolutionMaster",
                column: "CourseID");

            migrationBuilder.CreateIndex(
                name: "IX_ResolutionMaster_ExamID",
                table: "ResolutionMaster",
                column: "ExamID");

            migrationBuilder.AddForeignKey(
                name: "FK_ResolutionMaster_AcademicYear_AYID",
                table: "ResolutionMaster",
                column: "AYID",
                principalTable: "AcademicYear",
                principalColumn: "AYID");

            migrationBuilder.AddForeignKey(
                name: "FK_ResolutionMaster_CourseMaster_CourseID",
                table: "ResolutionMaster",
                column: "CourseID",
                principalTable: "CourseMaster",
                principalColumn: "CourseId");

            migrationBuilder.AddForeignKey(
                name: "FK_ResolutionMaster_ExamMaster_ExamID",
                table: "ResolutionMaster",
                column: "ExamID",
                principalTable: "ExamMaster",
                principalColumn: "ExamId");
        }
    }
}
