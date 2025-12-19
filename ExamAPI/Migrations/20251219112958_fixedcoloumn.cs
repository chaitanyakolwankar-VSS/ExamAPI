using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class fixedcoloumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RolePermission_RoleMaster_RoleMasterRoleId",
                table: "RolePermission");

            migrationBuilder.DropIndex(
                name: "IX_RolePermission_RoleMasterRoleId",
                table: "RolePermission");

            migrationBuilder.DropColumn(
                name: "RoleMasterRoleId",
                table: "RolePermission");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RoleMasterRoleId",
                table: "RolePermission",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePermission_RoleMasterRoleId",
                table: "RolePermission",
                column: "RoleMasterRoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_RolePermission_RoleMaster_RoleMasterRoleId",
                table: "RolePermission",
                column: "RoleMasterRoleId",
                principalTable: "RoleMaster",
                principalColumn: "RoleId");
        }
    }
}
