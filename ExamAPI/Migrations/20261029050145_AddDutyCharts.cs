using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddDutyCharts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StaffLetter_StaffId_ExamId_LetterType_SubjectId",
                table: "StaffLetter");

            migrationBuilder.AddColumn<bool>(
                name: "IsTeaching",
                table: "StaffMaster",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ExamId",
                table: "StaffLink",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "DutyChartId",
                table: "StaffLink",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "StaffLetterDuty",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "duty");

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "StaffLetterDuty",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ExamId",
                table: "StaffLetter",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "DutyChartId",
                table: "StaffLetter",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DutyGuidelines",
                table: "CollegeSettings",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportingPlace",
                table: "CollegeSettings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DutyChart",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AYID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Half = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FromDate = table.Column<DateTime>(type: "date", nullable: false),
                    ToDate = table.Column<DateTime>(type: "date", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DutyChart", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DutyChart_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DutyChartDay",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChartId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateTime>(type: "date", nullable: false),
                    Morning = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Afternoon = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FromTimetable = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DutyChartDay", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DutyChartDay_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DutyChartDay_DutyChart_ChartId",
                        column: x => x.ChartId,
                        principalTable: "DutyChart",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DutyChartEntry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChartId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateTime>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Session = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DutyChartEntry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DutyChartEntry_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DutyChartEntry_DutyChart_ChartId",
                        column: x => x.ChartId,
                        principalTable: "DutyChart",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DutyChartEntry_StaffMaster_StaffId",
                        column: x => x.StaffId,
                        principalTable: "StaffMaster",
                        principalColumn: "StaffId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffLetter_DutyChartId",
                table: "StaffLetter",
                column: "DutyChartId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffLetter_StaffId_ExamId_DutyChartId_LetterType_SubjectId",
                table: "StaffLetter",
                columns: new[] { "StaffId", "ExamId", "DutyChartId", "LetterType", "SubjectId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_DutyChart_CollegeId",
                table: "DutyChart",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_DutyChartDay_ChartId_Date",
                table: "DutyChartDay",
                columns: new[] { "ChartId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DutyChartDay_CollegeId",
                table: "DutyChartDay",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_DutyChartEntry_ChartId_StaffId_Date",
                table: "DutyChartEntry",
                columns: new[] { "ChartId", "StaffId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DutyChartEntry_CollegeId",
                table: "DutyChartEntry",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_DutyChartEntry_StaffId",
                table: "DutyChartEntry",
                column: "StaffId");

            migrationBuilder.AddForeignKey(
                name: "FK_StaffLetter_DutyChart_DutyChartId",
                table: "StaffLetter",
                column: "DutyChartId",
                principalTable: "DutyChart",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StaffLetter_DutyChart_DutyChartId",
                table: "StaffLetter");

            migrationBuilder.DropTable(
                name: "DutyChartDay");

            migrationBuilder.DropTable(
                name: "DutyChartEntry");

            migrationBuilder.DropTable(
                name: "DutyChart");

            migrationBuilder.DropIndex(
                name: "IX_StaffLetter_DutyChartId",
                table: "StaffLetter");

            migrationBuilder.DropIndex(
                name: "IX_StaffLetter_StaffId_ExamId_DutyChartId_LetterType_SubjectId",
                table: "StaffLetter");

            migrationBuilder.DropColumn(
                name: "IsTeaching",
                table: "StaffMaster");

            migrationBuilder.DropColumn(
                name: "DutyChartId",
                table: "StaffLink");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "StaffLetterDuty");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "StaffLetterDuty");

            migrationBuilder.DropColumn(
                name: "DutyChartId",
                table: "StaffLetter");

            migrationBuilder.DropColumn(
                name: "DutyGuidelines",
                table: "CollegeSettings");

            migrationBuilder.DropColumn(
                name: "ReportingPlace",
                table: "CollegeSettings");

            migrationBuilder.AlterColumn<Guid>(
                name: "ExamId",
                table: "StaffLink",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ExamId",
                table: "StaffLetter",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffLetter_StaffId_ExamId_LetterType_SubjectId",
                table: "StaffLetter",
                columns: new[] { "StaffId", "ExamId", "LetterType", "SubjectId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }
    }
}
