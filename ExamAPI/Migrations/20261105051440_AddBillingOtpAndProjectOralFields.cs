using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    // MeRate / MeCount / ProjectOral* columns are added by AddProjectOralAndMeRemuneration; this migration repeated them (fixed 2026-11-26 before any live run).
    public partial class AddBillingOtpAndProjectOralFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OtpCodeHash",
                table: "StaffBillingClaim",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OtpExpiresAt",
                table: "StaffBillingClaim",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OtpSentAt",
                table: "StaffBillingClaim",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OtpAttempts",
                table: "StaffBillingClaim",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "OtpVerifiedAt",
                table: "StaffBillingClaim",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffBillingClaim_Token",
                table: "StaffBillingClaim",
                column: "Token",
                unique: true,
                filter: "[Token] <> N''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StaffBillingClaim_Token",
                table: "StaffBillingClaim");

            migrationBuilder.DropColumn(
                name: "OtpCodeHash",
                table: "StaffBillingClaim");

            migrationBuilder.DropColumn(
                name: "OtpExpiresAt",
                table: "StaffBillingClaim");

            migrationBuilder.DropColumn(
                name: "OtpSentAt",
                table: "StaffBillingClaim");

            migrationBuilder.DropColumn(
                name: "OtpAttempts",
                table: "StaffBillingClaim");

            migrationBuilder.DropColumn(
                name: "OtpVerifiedAt",
                table: "StaffBillingClaim");
        }
    }
}