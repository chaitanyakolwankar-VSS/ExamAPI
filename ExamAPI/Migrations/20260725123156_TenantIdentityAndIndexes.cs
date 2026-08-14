using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class TenantIdentityAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserMaster_CollegeId",
                table: "UserMaster");

            migrationBuilder.DropIndex(
                name: "IX_UserMaster_Username",
                table: "UserMaster");

            migrationBuilder.DropIndex(
                name: "IX_StudentMaster_CollegeId",
                table: "StudentMaster");

            migrationBuilder.DropIndex(
                name: "IX_StudentMaster_StudentId",
                table: "StudentMaster");

            migrationBuilder.AddColumn<bool>(
                name: "IsPlatformAdmin",
                table: "UserMaster",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_UserMaster_CollegeId_Username",
                table: "UserMaster",
                columns: new[] { "CollegeId", "Username" },
                unique: true,
                filter: "[CollegeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StudentMaster_CollegeId_StudentId",
                table: "StudentMaster",
                columns: new[] { "CollegeId", "StudentId" },
                unique: true,
                filter: "[CollegeId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserMaster_CollegeId_Username",
                table: "UserMaster");

            migrationBuilder.DropIndex(
                name: "IX_StudentMaster_CollegeId_StudentId",
                table: "StudentMaster");

            migrationBuilder.DropColumn(
                name: "IsPlatformAdmin",
                table: "UserMaster");

            migrationBuilder.CreateIndex(
                name: "IX_UserMaster_CollegeId",
                table: "UserMaster",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMaster_Username",
                table: "UserMaster",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentMaster_CollegeId",
                table: "StudentMaster",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentMaster_StudentId",
                table: "StudentMaster",
                column: "StudentId",
                unique: true);
        }
    }
}
