using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddExamAssignmentPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExamAssignmentPolicy",
                columns: table => new
                {
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceExamTypes = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TargetExamTypes = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RequireFailedSubject = table.Column<bool>(type: "bit", nullable: false),
                    OfferPassedSubjects = table.Column<bool>(type: "bit", nullable: false),
                    BlockAbsentStudents = table.Column<bool>(type: "bit", nullable: false),
                    AutoSelectFailedSubjects = table.Column<bool>(type: "bit", nullable: false),
                    EligibleHeadTypes = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MaxSubjectsPerStudent = table.Column<int>(type: "int", nullable: true),
                    CarryForwardSeatNo = table.Column<bool>(type: "bit", nullable: false),
                    CarryForwardMarks = table.Column<bool>(type: "bit", nullable: false),
                    BlockDeleteAfterMarksEntry = table.Column<bool>(type: "bit", nullable: false),
                    RuleSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PatternId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SubjectsPerRow = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamAssignmentPolicy", x => x.PolicyId);
                    table.ForeignKey(
                        name: "FK_ExamAssignmentPolicy_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId");
                    table.ForeignKey(
                        name: "FK_ExamAssignmentPolicy_PatternMaster_PatternId",
                        column: x => x.PatternId,
                        principalTable: "PatternMaster",
                        principalColumn: "PatternId");
                    table.ForeignKey(
                        name: "FK_ExamAssignmentPolicy_RuleSet_RuleSetId",
                        column: x => x.RuleSetId,
                        principalTable: "RuleSet",
                        principalColumn: "RuleSetId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExamAssignmentPolicy_CollegeId",
                table: "ExamAssignmentPolicy",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamAssignmentPolicy_PatternId",
                table: "ExamAssignmentPolicy",
                column: "PatternId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamAssignmentPolicy_RuleSetId",
                table: "ExamAssignmentPolicy",
                column: "RuleSetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExamAssignmentPolicy");
        }
    }
}
