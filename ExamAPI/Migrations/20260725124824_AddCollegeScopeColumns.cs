using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddCollegeScopeColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CollegeId",
                table: "TimeTableMaster",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CollegeId",
                table: "SubjectMaster",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CollegeId",
                table: "SubjectCreditMaster",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CollegeId",
                table: "StudentsOverallResult",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CollegeId",
                table: "StudentEligibility",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CollegeId",
                table: "RuleSet",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CollegeId",
                table: "RoleMaster",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CollegeId",
                table: "ResolutionMaster",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CollegeId",
                table: "MarksMaster",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CollegeId",
                table: "GradeMaster",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CollegeId",
                table: "ExamMaster",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CollegeId",
                table: "AuditLog",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimeTableMaster_CollegeId",
                table: "TimeTableMaster",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_SubjectMaster_CollegeId",
                table: "SubjectMaster",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_SubjectCreditMaster_CollegeId",
                table: "SubjectCreditMaster",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentsOverallResult_CollegeId",
                table: "StudentsOverallResult",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentEligibility_CollegeId",
                table: "StudentEligibility",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_RuleSet_CollegeId",
                table: "RuleSet",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleMaster_CollegeId",
                table: "RoleMaster",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_ResolutionMaster_CollegeId",
                table: "ResolutionMaster",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_MarksMaster_CollegeId",
                table: "MarksMaster",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeMaster_CollegeId",
                table: "GradeMaster",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamMaster_CollegeId",
                table: "ExamMaster",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_CollegeId",
                table: "AuditLog",
                column: "CollegeId");

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLog_College_CollegeId",
                table: "AuditLog",
                column: "CollegeId",
                principalTable: "College",
                principalColumn: "CollegeId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExamMaster_College_CollegeId",
                table: "ExamMaster",
                column: "CollegeId",
                principalTable: "College",
                principalColumn: "CollegeId");

            migrationBuilder.AddForeignKey(
                name: "FK_GradeMaster_College_CollegeId",
                table: "GradeMaster",
                column: "CollegeId",
                principalTable: "College",
                principalColumn: "CollegeId");

            migrationBuilder.AddForeignKey(
                name: "FK_MarksMaster_College_CollegeId",
                table: "MarksMaster",
                column: "CollegeId",
                principalTable: "College",
                principalColumn: "CollegeId");

            migrationBuilder.AddForeignKey(
                name: "FK_ResolutionMaster_College_CollegeId",
                table: "ResolutionMaster",
                column: "CollegeId",
                principalTable: "College",
                principalColumn: "CollegeId");

            migrationBuilder.AddForeignKey(
                name: "FK_RoleMaster_College_CollegeId",
                table: "RoleMaster",
                column: "CollegeId",
                principalTable: "College",
                principalColumn: "CollegeId");

            migrationBuilder.AddForeignKey(
                name: "FK_RuleSet_College_CollegeId",
                table: "RuleSet",
                column: "CollegeId",
                principalTable: "College",
                principalColumn: "CollegeId");

            migrationBuilder.AddForeignKey(
                name: "FK_StudentEligibility_College_CollegeId",
                table: "StudentEligibility",
                column: "CollegeId",
                principalTable: "College",
                principalColumn: "CollegeId");

            migrationBuilder.AddForeignKey(
                name: "FK_StudentsOverallResult_College_CollegeId",
                table: "StudentsOverallResult",
                column: "CollegeId",
                principalTable: "College",
                principalColumn: "CollegeId");

            migrationBuilder.AddForeignKey(
                name: "FK_SubjectCreditMaster_College_CollegeId",
                table: "SubjectCreditMaster",
                column: "CollegeId",
                principalTable: "College",
                principalColumn: "CollegeId");

            migrationBuilder.AddForeignKey(
                name: "FK_SubjectMaster_College_CollegeId",
                table: "SubjectMaster",
                column: "CollegeId",
                principalTable: "College",
                principalColumn: "CollegeId");

            migrationBuilder.AddForeignKey(
                name: "FK_TimeTableMaster_College_CollegeId",
                table: "TimeTableMaster",
                column: "CollegeId",
                principalTable: "College",
                principalColumn: "CollegeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuditLog_College_CollegeId",
                table: "AuditLog");

            migrationBuilder.DropForeignKey(
                name: "FK_ExamMaster_College_CollegeId",
                table: "ExamMaster");

            migrationBuilder.DropForeignKey(
                name: "FK_GradeMaster_College_CollegeId",
                table: "GradeMaster");

            migrationBuilder.DropForeignKey(
                name: "FK_MarksMaster_College_CollegeId",
                table: "MarksMaster");

            migrationBuilder.DropForeignKey(
                name: "FK_ResolutionMaster_College_CollegeId",
                table: "ResolutionMaster");

            migrationBuilder.DropForeignKey(
                name: "FK_RoleMaster_College_CollegeId",
                table: "RoleMaster");

            migrationBuilder.DropForeignKey(
                name: "FK_RuleSet_College_CollegeId",
                table: "RuleSet");

            migrationBuilder.DropForeignKey(
                name: "FK_StudentEligibility_College_CollegeId",
                table: "StudentEligibility");

            migrationBuilder.DropForeignKey(
                name: "FK_StudentsOverallResult_College_CollegeId",
                table: "StudentsOverallResult");

            migrationBuilder.DropForeignKey(
                name: "FK_SubjectCreditMaster_College_CollegeId",
                table: "SubjectCreditMaster");

            migrationBuilder.DropForeignKey(
                name: "FK_SubjectMaster_College_CollegeId",
                table: "SubjectMaster");

            migrationBuilder.DropForeignKey(
                name: "FK_TimeTableMaster_College_CollegeId",
                table: "TimeTableMaster");

            migrationBuilder.DropIndex(
                name: "IX_TimeTableMaster_CollegeId",
                table: "TimeTableMaster");

            migrationBuilder.DropIndex(
                name: "IX_SubjectMaster_CollegeId",
                table: "SubjectMaster");

            migrationBuilder.DropIndex(
                name: "IX_SubjectCreditMaster_CollegeId",
                table: "SubjectCreditMaster");

            migrationBuilder.DropIndex(
                name: "IX_StudentsOverallResult_CollegeId",
                table: "StudentsOverallResult");

            migrationBuilder.DropIndex(
                name: "IX_StudentEligibility_CollegeId",
                table: "StudentEligibility");

            migrationBuilder.DropIndex(
                name: "IX_RuleSet_CollegeId",
                table: "RuleSet");

            migrationBuilder.DropIndex(
                name: "IX_RoleMaster_CollegeId",
                table: "RoleMaster");

            migrationBuilder.DropIndex(
                name: "IX_ResolutionMaster_CollegeId",
                table: "ResolutionMaster");

            migrationBuilder.DropIndex(
                name: "IX_MarksMaster_CollegeId",
                table: "MarksMaster");

            migrationBuilder.DropIndex(
                name: "IX_GradeMaster_CollegeId",
                table: "GradeMaster");

            migrationBuilder.DropIndex(
                name: "IX_ExamMaster_CollegeId",
                table: "ExamMaster");

            migrationBuilder.DropIndex(
                name: "IX_AuditLog_CollegeId",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "CollegeId",
                table: "TimeTableMaster");

            migrationBuilder.DropColumn(
                name: "CollegeId",
                table: "SubjectMaster");

            migrationBuilder.DropColumn(
                name: "CollegeId",
                table: "SubjectCreditMaster");

            migrationBuilder.DropColumn(
                name: "CollegeId",
                table: "StudentsOverallResult");

            migrationBuilder.DropColumn(
                name: "CollegeId",
                table: "StudentEligibility");

            migrationBuilder.DropColumn(
                name: "CollegeId",
                table: "RuleSet");

            migrationBuilder.DropColumn(
                name: "CollegeId",
                table: "RoleMaster");

            migrationBuilder.DropColumn(
                name: "CollegeId",
                table: "ResolutionMaster");

            migrationBuilder.DropColumn(
                name: "CollegeId",
                table: "MarksMaster");

            migrationBuilder.DropColumn(
                name: "CollegeId",
                table: "GradeMaster");

            migrationBuilder.DropColumn(
                name: "CollegeId",
                table: "ExamMaster");

            migrationBuilder.DropColumn(
                name: "CollegeId",
                table: "AuditLog");
        }
    }
}
