using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddSgpiCgpiAndDynamicRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AYID",
                table: "MarksMaster",
                newName: "QuotaType");

            migrationBuilder.AddColumn<decimal>(
                name: "CGPI",
                table: "StudentsOverallResult",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SGPI",
                table: "StudentsOverallResult",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CGPI",
                table: "MarksMaster",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HMCheck",
                table: "MarksMaster",
                type: "bit",
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

            migrationBuilder.CreateTable(
                name: "ResolutionMaster",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreditID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubjectCreditID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Head = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Resolution = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CourseID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AYID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResolutionMaster", x => x.ID);
                    table.ForeignKey(
                        name: "FK_ResolutionMaster_AcademicYear_AYID",
                        column: x => x.AYID,
                        principalTable: "AcademicYear",
                        principalColumn: "AYID");
                    table.ForeignKey(
                        name: "FK_ResolutionMaster_CourseMaster_CourseID",
                        column: x => x.CourseID,
                        principalTable: "CourseMaster",
                        principalColumn: "CourseId");
                    table.ForeignKey(
                        name: "FK_ResolutionMaster_ExamMaster_ExamID",
                        column: x => x.ExamID,
                        principalTable: "ExamMaster",
                        principalColumn: "ExamId");
                    table.ForeignKey(
                        name: "FK_ResolutionMaster_SubjectCreditMaster_CreditID",
                        column: x => x.CreditID,
                        principalTable: "SubjectCreditMaster",
                        principalColumn: "CreditsId");
                    table.ForeignKey(
                        name: "FK_ResolutionMaster_SubjectCredits_SubjectCreditID",
                        column: x => x.SubjectCreditID,
                        principalTable: "SubjectCredits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResolutionMaster_AYID",
                table: "ResolutionMaster",
                column: "AYID");

            migrationBuilder.CreateIndex(
                name: "IX_ResolutionMaster_CourseID",
                table: "ResolutionMaster",
                column: "CourseID");

            migrationBuilder.CreateIndex(
                name: "IX_ResolutionMaster_CreditID",
                table: "ResolutionMaster",
                column: "CreditID");

            migrationBuilder.CreateIndex(
                name: "IX_ResolutionMaster_ExamID",
                table: "ResolutionMaster",
                column: "ExamID");

            migrationBuilder.CreateIndex(
                name: "IX_ResolutionMaster_SubjectCreditID",
                table: "ResolutionMaster",
                column: "SubjectCreditID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResolutionMaster");

            migrationBuilder.DropColumn(
                name: "CGPI",
                table: "StudentsOverallResult");

            migrationBuilder.DropColumn(
                name: "SGPI",
                table: "StudentsOverallResult");

            migrationBuilder.DropColumn(
                name: "CGPI",
                table: "MarksMaster");

            migrationBuilder.DropColumn(
                name: "HMCheck",
                table: "MarksMaster");

            migrationBuilder.DropColumn(
                name: "ResultRemark",
                table: "MarksMaster");

            migrationBuilder.DropColumn(
                name: "SGPI",
                table: "MarksMaster");

            migrationBuilder.RenameColumn(
                name: "QuotaType",
                table: "MarksMaster",
                newName: "AYID");
        }
    }
}
