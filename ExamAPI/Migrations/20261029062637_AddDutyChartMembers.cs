using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddDutyChartMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DutyChartMember",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChartId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DutyChartMember", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DutyChartMember_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DutyChartMember_DutyChart_ChartId",
                        column: x => x.ChartId,
                        principalTable: "DutyChart",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DutyChartMember_StaffMaster_StaffId",
                        column: x => x.StaffId,
                        principalTable: "StaffMaster",
                        principalColumn: "StaffId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DutyChartMember_ChartId_StaffId",
                table: "DutyChartMember",
                columns: new[] { "ChartId", "StaffId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DutyChartMember_CollegeId",
                table: "DutyChartMember",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_DutyChartMember_StaffId",
                table: "DutyChartMember",
                column: "StaffId");

            // Charts that already have entries keep their people (ordered by name); empty charts stay empty.
            migrationBuilder.Sql(@"
INSERT INTO DutyChartMember (Id, CollegeId, ChartId, StaffId, SortOrder)
SELECT NEWID(), x.CollegeId, x.ChartId, x.StaffId,
       ROW_NUMBER() OVER (PARTITION BY x.ChartId ORDER BY s.FirstName, s.LastName, x.StaffId) - 1
FROM (SELECT DISTINCT ChartId, StaffId, CollegeId FROM DutyChartEntry) x
JOIN StaffMaster s ON s.StaffId = x.StaffId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DutyChartMember");
        }
    }
}
