using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddOrdinanceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RuleSet_PatternMaster_PatternId",
                table: "RuleSet");

            migrationBuilder.DropColumn(
                name: "AYID",
                table: "ExamMaster");

            migrationBuilder.AlterColumn<Guid>(
                name: "AYID",
                table: "StudentEligibility",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PatternId",
                table: "RuleSet",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrdinanceSymbol",
                table: "Rule",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "StopOnSuccess",
                table: "Rule",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Pattern",
                table: "MarksMaster",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "ExamMaster",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RevaluationForExamId",
                table: "ExamMaster",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RuleSet_PatternMaster_PatternId",
                table: "RuleSet",
                column: "PatternId",
                principalTable: "PatternMaster",
                principalColumn: "PatternId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RuleSet_PatternMaster_PatternId",
                table: "RuleSet");

            migrationBuilder.DropColumn(
                name: "OrdinanceSymbol",
                table: "Rule");

            migrationBuilder.DropColumn(
                name: "StopOnSuccess",
                table: "Rule");

            migrationBuilder.DropColumn(
                name: "Pattern",
                table: "MarksMaster");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "ExamMaster");

            migrationBuilder.DropColumn(
                name: "RevaluationForExamId",
                table: "ExamMaster");

            migrationBuilder.AlterColumn<string>(
                name: "AYID",
                table: "StudentEligibility",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PatternId",
                table: "RuleSet",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<string>(
                name: "AYID",
                table: "ExamMaster",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RuleSet_PatternMaster_PatternId",
                table: "RuleSet",
                column: "PatternId",
                principalTable: "PatternMaster",
                principalColumn: "PatternId");
        }
    }
}
