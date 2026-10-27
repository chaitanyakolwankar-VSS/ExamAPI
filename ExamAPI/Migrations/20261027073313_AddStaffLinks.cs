using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StaffLink",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DetailsConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DownloadedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LinkBaseUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffLink", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffLink_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffLink_ExamMaster_ExamId",
                        column: x => x.ExamId,
                        principalTable: "ExamMaster",
                        principalColumn: "ExamId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffLink_StaffMaster_StaffId",
                        column: x => x.StaffId,
                        principalTable: "StaffMaster",
                        principalColumn: "StaffId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffOtp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeHash = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffOtp", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffOtp_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffOtp_StaffLink_LinkId",
                        column: x => x.LinkId,
                        principalTable: "StaffLink",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffSession",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    LinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffSession", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffSession_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffSession_StaffLink_LinkId",
                        column: x => x.LinkId,
                        principalTable: "StaffLink",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffSession_StaffMaster_StaffId",
                        column: x => x.StaffId,
                        principalTable: "StaffMaster",
                        principalColumn: "StaffId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffLink_CollegeId",
                table: "StaffLink",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffLink_ExamId",
                table: "StaffLink",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffLink_StaffId_ExamId_Status",
                table: "StaffLink",
                columns: new[] { "StaffId", "ExamId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffLink_TokenHash",
                table: "StaffLink",
                column: "TokenHash",
                unique: true,
                filter: "[TokenHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StaffOtp_CollegeId",
                table: "StaffOtp",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffOtp_LinkId_CreatedAt",
                table: "StaffOtp",
                columns: new[] { "LinkId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffSession_CollegeId",
                table: "StaffSession",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffSession_LinkId",
                table: "StaffSession",
                column: "LinkId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffSession_SessionHash",
                table: "StaffSession",
                column: "SessionHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffSession_StaffId",
                table: "StaffSession",
                column: "StaffId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffOtp");

            migrationBuilder.DropTable(
                name: "StaffSession");

            migrationBuilder.DropTable(
                name: "StaffLink");
        }
    }
}
