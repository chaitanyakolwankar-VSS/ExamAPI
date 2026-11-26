using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
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

            migrationBuilder.AddColumn<decimal>(
                name: "MeRate",
                table: "StaffBillItem",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "MeCount",
                table: "StaffBillItem",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "ProjectOralRate",
                table: "StaffBillItem",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ProjectOralCount",
                table: "StaffBillItem",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "MeRate",
                table: "BillingPriceSet",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ProjectOralRate",
                table: "BillingPriceSet",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

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

            migrationBuilder.DropColumn(
                name: "MeRate",
                table: "StaffBillItem");

            migrationBuilder.DropColumn(
                name: "MeCount",
                table: "StaffBillItem");

            migrationBuilder.DropColumn(
                name: "ProjectOralCount",
                table: "StaffBillItem");

            migrationBuilder.DropColumn(
                name: "ProjectOralRate",
                table: "StaffBillItem");

            migrationBuilder.DropColumn(
                name: "MeRate",
                table: "BillingPriceSet");

            migrationBuilder.DropColumn(
                name: "ProjectOralRate",
                table: "BillingPriceSet");
        }
    }
}