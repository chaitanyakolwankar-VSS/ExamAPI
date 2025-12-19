using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddedModulePermisson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PermissionName",
                table: "Permission",
                newName: "PermissionModuleName");

            migrationBuilder.AddColumn<string>(
                name: "PermissionFormName",
                table: "Permission",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PermissionFormName",
                table: "Permission");

            migrationBuilder.RenameColumn(
                name: "PermissionModuleName",
                table: "Permission",
                newName: "PermissionName");
        }
    }
}
