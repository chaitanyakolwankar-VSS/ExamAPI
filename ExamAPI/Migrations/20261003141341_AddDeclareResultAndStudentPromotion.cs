using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddDeclareResultAndStudentPromotion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsEligible",
                table: "StudentEligibility",
                type: "bit",
                nullable: false,
                defaultValue: true); // existing eligibility rows are current, eligible students (owner, 2026-10-03)

            migrationBuilder.CreateTable(
                name: "DeclareResult",
                columns: table => new
                {
                    DeclareID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Sem_id = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsDeclare = table.Column<bool>(type: "bit", nullable: false),
                    CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcademicYear = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeclareDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GazetteGnrt = table.Column<int>(type: "int", nullable: false),
                    Pattern = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReleaseHallTicket = table.Column<bool>(type: "bit", nullable: false),
                    HallTicketDeclareDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HallTicketUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GazetteDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResDeclare = table.Column<int>(type: "int", nullable: false),
                    ResDeclareDateTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeclareResult", x => x.DeclareID);
                    table.ForeignKey(
                        name: "FK_DeclareResult_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeclareResult_CollegeId",
                table: "DeclareResult",
                column: "CollegeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeclareResult");

            migrationBuilder.DropColumn(
                name: "IsEligible",
                table: "StudentEligibility");
        }
    }
}
