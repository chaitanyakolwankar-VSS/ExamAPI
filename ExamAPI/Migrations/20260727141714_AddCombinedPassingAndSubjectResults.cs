using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddCombinedPassingAndSubjectResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PassPercentage",
                table: "SubjectCreditMaster",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PassingStrategy",
                table: "SubjectCreditMaster",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                // Existing subjects keep today's behaviour; combined is opt-in per subject.
                defaultValue: "HeadWise");

            migrationBuilder.AddColumn<bool>(
                name: "IsAbsent",
                table: "StudentMarks",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "StudentSubjectResult",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObtainedTotal = table.Column<int>(type: "int", nullable: false),
                    RawObtainedTotal = table.Column<int>(type: "int", nullable: false),
                    OutOfTotal = table.Column<int>(type: "int", nullable: false),
                    GraceApplied = table.Column<int>(type: "int", nullable: false),
                    GraceSymbol = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Grade = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    GradePoint = table.Column<int>(type: "int", nullable: false),
                    RawGradePoint = table.Column<int>(type: "int", nullable: false),
                    IsPassed = table.Column<bool>(type: "bit", nullable: false),
                    SubjectStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MarksId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditsId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentSubjectResult", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentSubjectResult_MarksMaster_MarksId",
                        column: x => x.MarksId,
                        principalTable: "MarksMaster",
                        principalColumn: "MarksId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudentSubjectResult_SubjectCreditMaster_CreditsId",
                        column: x => x.CreditsId,
                        principalTable: "SubjectCreditMaster",
                        principalColumn: "CreditsId");
                    table.ForeignKey(
                        name: "FK_StudentSubjectResult_SubjectMaster_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "SubjectMaster",
                        principalColumn: "SubjectId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudentSubjectResult_CreditsId",
                table: "StudentSubjectResult",
                column: "CreditsId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentSubjectResult_MarksId_SubjectId",
                table: "StudentSubjectResult",
                columns: new[] { "MarksId", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentSubjectResult_SubjectId",
                table: "StudentSubjectResult",
                column: "SubjectId");

            // Absence was previously encoded in the overloaded Remark column.
            migrationBuilder.Sql(@"
                UPDATE StudentMarks SET IsAbsent = 1 WHERE Remark = 'Ab' AND IsDeleted = 0;");

            // The combined threshold used to be duplicated onto every head as HeadFormula.
            migrationBuilder.Sql(@"
                UPDATE cm
                SET cm.PassPercentage = h.Formula
                FROM SubjectCreditMaster cm
                CROSS APPLY (
                    SELECT TOP 1 TRY_CAST(sc.HeadFormula AS int) AS Formula
                    FROM SubjectCredits sc
                    WHERE sc.CreditsId = cm.CreditsId
                      AND sc.IsDeleted = 0
                      AND TRY_CAST(sc.HeadFormula AS int) IS NOT NULL
                ) h
                WHERE cm.PassPercentage IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudentSubjectResult");

            migrationBuilder.DropColumn(
                name: "PassPercentage",
                table: "SubjectCreditMaster");

            migrationBuilder.DropColumn(
                name: "PassingStrategy",
                table: "SubjectCreditMaster");

            migrationBuilder.DropColumn(
                name: "IsAbsent",
                table: "StudentMarks");
        }
    }
}
