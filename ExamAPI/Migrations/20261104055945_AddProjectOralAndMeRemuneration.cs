using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectOralAndMeRemuneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MeCount",
                table: "StaffBillItem",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "MeRate",
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
                name: "ProjectOralRate",
                table: "StaffBillItem",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MeCount",
                table: "StaffBillItem");

            migrationBuilder.DropColumn(
                name: "MeRate",
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
