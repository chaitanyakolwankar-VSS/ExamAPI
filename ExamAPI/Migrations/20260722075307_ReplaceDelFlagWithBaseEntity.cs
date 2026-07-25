using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceDelFlagWithBaseEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "DeclareResult",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                table: "DeclareResult",
                type: "uniqueidentifier",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Del_flag",
                table: "DeclareResult",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "DeclareResult",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "DeclareResult",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "DeclareResult",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "DeclareResult");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "DeclareResult");

            migrationBuilder.DropColumn(
                name: "Del_flag",
                table: "DeclareResult");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "DeclareResult");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "DeclareResult");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "DeclareResult");
        }
    }
}
