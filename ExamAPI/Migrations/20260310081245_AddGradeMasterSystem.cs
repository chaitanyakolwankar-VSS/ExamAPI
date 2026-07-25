using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddGradeMasterSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GradeMasterId",
                table: "RuleSet",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GradeMaster",
                columns: table => new
                {
                    GradeMasterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeMaster", x => x.GradeMasterId);
                });

            migrationBuilder.CreateTable(
                name: "GradeThreshold",
                columns: table => new
                {
                    ThresholdId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Grade = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    GradePoint = table.Column<int>(type: "int", nullable: false),
                    MinPercentage = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    MaxPercentage = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    PerformanceRemark = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    GradeMasterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeThreshold", x => x.ThresholdId);
                    table.ForeignKey(
                        name: "FK_GradeThreshold_GradeMaster_GradeMasterId",
                        column: x => x.GradeMasterId,
                        principalTable: "GradeMaster",
                        principalColumn: "GradeMasterId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RuleSet_GradeMasterId",
                table: "RuleSet",
                column: "GradeMasterId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeThreshold_GradeMasterId",
                table: "GradeThreshold",
                column: "GradeMasterId");

            migrationBuilder.AddForeignKey(
                name: "FK_RuleSet_GradeMaster_GradeMasterId",
                table: "RuleSet",
                column: "GradeMasterId",
                principalTable: "GradeMaster",
                principalColumn: "GradeMasterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RuleSet_GradeMaster_GradeMasterId",
                table: "RuleSet");

            migrationBuilder.DropTable(
                name: "GradeThreshold");

            migrationBuilder.DropTable(
                name: "GradeMaster");

            migrationBuilder.DropIndex(
                name: "IX_RuleSet_GradeMasterId",
                table: "RuleSet");

            migrationBuilder.DropColumn(
                name: "GradeMasterId",
                table: "RuleSet");
        }
    }
}
