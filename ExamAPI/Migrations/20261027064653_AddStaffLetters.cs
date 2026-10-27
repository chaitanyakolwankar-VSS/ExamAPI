using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffLetters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CapContact",
                table: "CollegeSettings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LetterPrefix",
                table: "CollegeSettings",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StaffLetter",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LetterType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AYID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SerialNo = table.Column<int>(type: "int", nullable: false),
                    LetterNo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    LetterDate = table.Column<DateTime>(type: "date", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    StaffName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ExamTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Half = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffLetter", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffLetter_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffLetter_ExamMaster_ExamId",
                        column: x => x.ExamId,
                        principalTable: "ExamMaster",
                        principalColumn: "ExamId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffLetter_StaffMaster_StaffId",
                        column: x => x.StaffId,
                        principalTable: "StaffMaster",
                        principalColumn: "StaffId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffLetterDuty",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LetterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Time = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffLetterDuty", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffLetterDuty_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffLetterDuty_StaffLetter_LetterId",
                        column: x => x.LetterId,
                        principalTable: "StaffLetter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffLetter_CollegeId_LetterType_AYID_SerialNo",
                table: "StaffLetter",
                columns: new[] { "CollegeId", "LetterType", "AYID", "SerialNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_StaffLetter_ExamId",
                table: "StaffLetter",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffLetter_StaffId_ExamId_LetterType_SubjectId",
                table: "StaffLetter",
                columns: new[] { "StaffId", "ExamId", "LetterType", "SubjectId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_StaffLetterDuty_CollegeId",
                table: "StaffLetterDuty",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffLetterDuty_LetterId",
                table: "StaffLetterDuty",
                column: "LetterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffLetterDuty");

            migrationBuilder.DropTable(
                name: "StaffLetter");

            migrationBuilder.DropColumn(
                name: "CapContact",
                table: "CollegeSettings");

            migrationBuilder.DropColumn(
                name: "LetterPrefix",
                table: "CollegeSettings");
        }
    }
}
