using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLeaveFieldsFromStaffBillingClaim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LeaveDays",
                table: "StaffBillingClaim");

            migrationBuilder.DropColumn(
                name: "LeaveDatesAndReason",
                table: "StaffBillingClaim");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LeaveDays",
                table: "StaffBillingClaim",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LeaveDatesAndReason",
                table: "StaffBillingClaim",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }
    }
}

