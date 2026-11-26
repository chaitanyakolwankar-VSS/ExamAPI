using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingClaimIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_StaffBillingClaim_DutyChartId_StaffId",
                table: "StaffBillingClaim",
                columns: new[] { "DutyChartId", "StaffId" });

            // AddBillingOtpAndProjectOralFields already made this index (filter [Token] <> ''), but the model snapshot
            // missed it: replace it with the model's version.
            migrationBuilder.DropIndex(
                name: "IX_StaffBillingClaim_Token",
                table: "StaffBillingClaim");

            migrationBuilder.CreateIndex(
                name: "IX_StaffBillingClaim_Token",
                table: "StaffBillingClaim",
                column: "Token",
                unique: true,
                filter: "[Token] IS NOT NULL AND [Token] <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StaffBillingClaim_DutyChartId_StaffId",
                table: "StaffBillingClaim");

            migrationBuilder.DropIndex(
                name: "IX_StaffBillingClaim_Token",
                table: "StaffBillingClaim");

            migrationBuilder.CreateIndex(
                name: "IX_StaffBillingClaim_Token",
                table: "StaffBillingClaim",
                column: "Token",
                unique: true,
                filter: "[Token] <> ''");
        }
    }
}
