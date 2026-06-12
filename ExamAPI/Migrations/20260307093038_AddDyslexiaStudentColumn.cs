using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddDyslexiaStudentColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
  

            migrationBuilder.AddColumn<Guid>(
                name: "StudentMasterStdMstId",
                table: "CourseMaster",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseMaster_StudentMasterStdMstId",
                table: "CourseMaster",
                column: "StudentMasterStdMstId");

            migrationBuilder.AddForeignKey(
                name: "FK_CourseMaster_StudentMaster_StudentMasterStdMstId",
                table: "CourseMaster",
                column: "StudentMasterStdMstId",
                principalTable: "StudentMaster",
                principalColumn: "StdMstId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CourseMaster_StudentMaster_StudentMasterStdMstId",
                table: "CourseMaster");

            migrationBuilder.DropIndex(
                name: "IX_CourseMaster_StudentMasterStdMstId",
                table: "CourseMaster");

          

            migrationBuilder.DropColumn(
                name: "StudentMasterStdMstId",
                table: "CourseMaster");
        }
    }
}
