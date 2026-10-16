using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmailFlagReason",
                table: "StudentMaster",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailStatus",
                table: "StudentMaster",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "StudentMaster",
                type: "bit",
                nullable: false,
                defaultValue: true); // existing students are active (hand-edited: EF suggests false)

            migrationBuilder.AddColumn<string>(
                name: "Details",
                table: "AuditLog",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Event",
                table: "AuditLog",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CollegeSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AllowedEmailDomains = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ExamOfficeEmail = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    EmailSenderKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DailyEmailLimit = table.Column<int>(type: "int", nullable: false, defaultValue: 2000),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollegeSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CollegeSettings_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmailQueue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RefId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ToAddress = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    HtmlBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TextBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailQueue", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailQueue_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaskType",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExpiryRule = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequiresOtp = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskType", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "TaskBatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TaskTypeCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Deadline = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SemesterId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AYID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskBatch", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskBatch_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskBatch_TaskType_TaskTypeCode",
                        column: x => x.TaskTypeCode,
                        principalTable: "TaskType",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaskLink",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TaskTypeCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StdMstId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RefId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TokenHash = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskLink", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskLink_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskLink_StudentMaster_StdMstId",
                        column: x => x.StdMstId,
                        principalTable: "StudentMaster",
                        principalColumn: "StdMstId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskLink_TaskBatch_BatchId",
                        column: x => x.BatchId,
                        principalTable: "TaskBatch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskLink_TaskType_TaskTypeCode",
                        column: x => x.TaskTypeCode,
                        principalTable: "TaskType",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OtpChallenge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TaskLinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeHash = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtpChallenge", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OtpChallenge_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OtpChallenge_TaskLink_TaskLinkId",
                        column: x => x.TaskLinkId,
                        principalTable: "TaskLink",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StudentSession",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    TaskLinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StdMstId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskTypeCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentSession", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentSession_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentSession_StudentMaster_StdMstId",
                        column: x => x.StdMstId,
                        principalTable: "StudentMaster",
                        principalColumn: "StdMstId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentSession_TaskLink_TaskLinkId",
                        column: x => x.TaskLinkId,
                        principalTable: "TaskLink",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "TaskType",
                columns: new[] { "Code", "ExpiryRule", "Name", "RequiresOtp" },
                values: new object[,]
                {
                    { "ATKT_PAYMENT", "DIE_ON_PAYMENT", "Pay ATKT exam fee", true },
                    { "HALL_TICKET", "DIE_ON_DEADLINE", "Download hall ticket", true },
                    { "MARKSHEET", "DIE_ON_DEADLINE", "Download marksheet", true },
                    { "STUDENT_MASTER", "DIE_ON_DEADLINE", "Verify student details", true }
                });

            // Before the one-email-per-college index: store emails trimmed and lower-case, blanks as NULL, and
            // flag (not drop) a repeat -- the first student keeps the email, the others get it in the reason.
            // EXEC: in the idempotent script this sits in one batch with the ALTERs, which SQL Server compiles first.
            migrationBuilder.Sql(@"
EXEC(N'UPDATE StudentMaster SET Email = NULLIF(LOWER(LTRIM(RTRIM(Email))), '''') WHERE Email IS NOT NULL;
WITH d AS (
    SELECT StdMstId, Email, ROW_NUMBER() OVER (PARTITION BY CollegeId, Email ORDER BY CreatedAt, StdMstId) AS n
    FROM StudentMaster WHERE Email IS NOT NULL AND IsDeleted = 0)
UPDATE s SET s.EmailStatus = ''Flagged'', s.EmailFlagReason = LEFT(''Same email as another student: '' + d.Email, 200), s.Email = NULL
FROM StudentMaster s JOIN d ON d.StdMstId = s.StdMstId WHERE d.n > 1;');");

            migrationBuilder.CreateIndex(
                name: "IX_StudentMaster_CollegeId_Email",
                table: "StudentMaster",
                columns: new[] { "CollegeId", "Email" },
                unique: true,
                filter: "[Email] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CollegeSettings_CollegeId",
                table: "CollegeSettings",
                column: "CollegeId",
                unique: true,
                filter: "[CollegeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EmailQueue_CollegeId_SentAt",
                table: "EmailQueue",
                columns: new[] { "CollegeId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailQueue_Status_Priority_NextAttemptAt",
                table: "EmailQueue",
                columns: new[] { "Status", "Priority", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenge_CollegeId",
                table: "OtpChallenge",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenge_TaskLinkId_CreatedAt",
                table: "OtpChallenge",
                columns: new[] { "TaskLinkId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentSession_CollegeId",
                table: "StudentSession",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentSession_SessionHash",
                table: "StudentSession",
                column: "SessionHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentSession_StdMstId",
                table: "StudentSession",
                column: "StdMstId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentSession_TaskLinkId",
                table: "StudentSession",
                column: "TaskLinkId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskBatch_CollegeId",
                table: "TaskBatch",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskBatch_TaskTypeCode",
                table: "TaskBatch",
                column: "TaskTypeCode");

            migrationBuilder.CreateIndex(
                name: "IX_TaskLink_BatchId",
                table: "TaskLink",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskLink_CollegeId",
                table: "TaskLink",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskLink_StdMstId_TaskTypeCode_Status",
                table: "TaskLink",
                columns: new[] { "StdMstId", "TaskTypeCode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskLink_TaskTypeCode",
                table: "TaskLink",
                column: "TaskTypeCode");

            migrationBuilder.CreateIndex(
                name: "IX_TaskLink_TokenHash",
                table: "TaskLink",
                column: "TokenHash",
                unique: true,
                filter: "[TokenHash] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CollegeSettings");

            migrationBuilder.DropTable(
                name: "EmailQueue");

            migrationBuilder.DropTable(
                name: "OtpChallenge");

            migrationBuilder.DropTable(
                name: "StudentSession");

            migrationBuilder.DropTable(
                name: "TaskLink");

            migrationBuilder.DropTable(
                name: "TaskBatch");

            migrationBuilder.DropTable(
                name: "TaskType");

            migrationBuilder.DropIndex(
                name: "IX_StudentMaster_CollegeId_Email",
                table: "StudentMaster");

            migrationBuilder.DropColumn(
                name: "EmailFlagReason",
                table: "StudentMaster");

            migrationBuilder.DropColumn(
                name: "EmailStatus",
                table: "StudentMaster");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "StudentMaster");

            migrationBuilder.DropColumn(
                name: "Details",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "Event",
                table: "AuditLog");
        }
    }
}
