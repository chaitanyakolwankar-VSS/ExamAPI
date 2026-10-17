using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailSenderKey",
                table: "CollegeSettings");

            migrationBuilder.CreateTable(
                name: "EmailAccount",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Host = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Port = table.Column<int>(type: "int", nullable: false),
                    UseStartTls = table.Column<bool>(type: "bit", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    PasswordProtected = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FromAddress = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FromName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailAccount", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailAccount_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailAccount_CollegeId",
                table: "EmailAccount",
                column: "CollegeId",
                unique: true,
                filter: "[CollegeId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailAccount");

            migrationBuilder.AddColumn<string>(
                name: "EmailSenderKey",
                table: "CollegeSettings",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }
    }
}
