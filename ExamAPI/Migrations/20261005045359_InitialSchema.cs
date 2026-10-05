using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "College",
                columns: table => new
                {
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CollegeCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CollegeCenter = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LogoUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LogoBannerUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ControllerSignUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PrincipalSignUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ContactEmail = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContactPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_College", x => x.CollegeId);
                });

            migrationBuilder.CreateTable(
                name: "Permission",
                columns: table => new
                {
                    PermissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionFormName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PermissionModuleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permission", x => x.PermissionId);
                });

            migrationBuilder.CreateTable(
                name: "AcademicYear",
                columns: table => new
                {
                    AYID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullDuration = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ShortDuration = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcademicYear", x => x.AYID);
                    table.ForeignKey(
                        name: "FK_AcademicYear_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TableName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RecordId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PerformedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLog_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId");
                });

            migrationBuilder.CreateTable(
                name: "DeclareResult",
                columns: table => new
                {
                    DeclareID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Sem_id = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsDeclare = table.Column<bool>(type: "bit", nullable: false),
                    CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcademicYear = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeclareDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GazetteGnrt = table.Column<int>(type: "int", nullable: false),
                    Pattern = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReleaseHallTicket = table.Column<bool>(type: "bit", nullable: false),
                    HallTicketDeclareDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HallTicketUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GazetteDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResDeclare = table.Column<int>(type: "int", nullable: false),
                    ResDeclareDateTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeclareResult", x => x.DeclareID);
                    table.ForeignKey(
                        name: "FK_DeclareResult_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId");
                });

            migrationBuilder.CreateTable(
                name: "GraceLookup",
                columns: table => new
                {
                    GraceLookupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HeadMarksUpto = table.Column<int>(type: "int", nullable: false),
                    GraceMarks = table.Column<int>(type: "int", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GraceLookup", x => x.GraceLookupId);
                    table.ForeignKey(
                        name: "FK_GraceLookup_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId");
                });

            migrationBuilder.CreateTable(
                name: "GradeMaster",
                columns: table => new
                {
                    GradeMasterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeMaster", x => x.GradeMasterId);
                    table.ForeignKey(
                        name: "FK_GradeMaster_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId");
                });

            migrationBuilder.CreateTable(
                name: "PatternMaster",
                columns: table => new
                {
                    PatternId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatternName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatternMaster", x => x.PatternId);
                    table.ForeignKey(
                        name: "FK_PatternMaster_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId");
                });

            migrationBuilder.CreateTable(
                name: "RoleMaster",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleMaster", x => x.RoleId);
                    table.ForeignKey(
                        name: "FK_RoleMaster_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId");
                });

            migrationBuilder.CreateTable(
                name: "StudentMaster",
                columns: table => new
                {
                    StdMstId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    MiddleName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Gender = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    StudentPRN = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PhotoUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SignUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DateOfBirth = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DyslexiaStudent = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentMaster", x => x.StdMstId);
                    table.ForeignKey(
                        name: "FK_StudentMaster_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId");
                });

            migrationBuilder.CreateTable(
                name: "GradeThreshold",
                columns: table => new
                {
                    ThresholdId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Grade = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    GradePoint = table.Column<int>(type: "int", nullable: false),
                    MinPercentage = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    MaxPercentage = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    PerformanceRemark = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    GradeMasterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeThreshold", x => x.ThresholdId);
                    table.ForeignKey(
                        name: "FK_GradeThreshold_GradeMaster_GradeMasterId",
                        column: x => x.GradeMasterId,
                        principalTable: "GradeMaster",
                        principalColumn: "GradeMasterId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RuleSet",
                columns: table => new
                {
                    RuleSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExamType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PatternId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GradeMasterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleSet", x => x.RuleSetId);
                    table.ForeignKey(
                        name: "FK_RuleSet_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId");
                    table.ForeignKey(
                        name: "FK_RuleSet_GradeMaster_GradeMasterId",
                        column: x => x.GradeMasterId,
                        principalTable: "GradeMaster",
                        principalColumn: "GradeMasterId");
                    table.ForeignKey(
                        name: "FK_RuleSet_PatternMaster_PatternId",
                        column: x => x.PatternId,
                        principalTable: "PatternMaster",
                        principalColumn: "PatternId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RolePermission",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermission", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_RolePermission_Permission_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permission",
                        principalColumn: "PermissionId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RolePermission_RoleMaster_RoleId",
                        column: x => x.RoleId,
                        principalTable: "RoleMaster",
                        principalColumn: "RoleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserMaster",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    HashedPassword = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsPlatformAdmin = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMaster", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserMaster_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId");
                    table.ForeignKey(
                        name: "FK_UserMaster_RoleMaster_RoleId",
                        column: x => x.RoleId,
                        principalTable: "RoleMaster",
                        principalColumn: "RoleId");
                });

            migrationBuilder.CreateTable(
                name: "CourseMaster",
                columns: table => new
                {
                    CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CourseCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StudentMasterStdMstId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseMaster", x => x.CourseId);
                    table.ForeignKey(
                        name: "FK_CourseMaster_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId");
                    table.ForeignKey(
                        name: "FK_CourseMaster_StudentMaster_StudentMasterStdMstId",
                        column: x => x.StudentMasterStdMstId,
                        principalTable: "StudentMaster",
                        principalColumn: "StdMstId");
                });

            migrationBuilder.CreateTable(
                name: "StudentsOverallResult",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SemesterId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CreditGradePoint = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Credits = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SGPI = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CGPI = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    KtTheory = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    KtOther = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    StdMstId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentsOverallResult", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentsOverallResult_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId");
                    table.ForeignKey(
                        name: "FK_StudentsOverallResult_StudentMaster_StdMstId",
                        column: x => x.StdMstId,
                        principalTable: "StudentMaster",
                        principalColumn: "StdMstId");
                });

            migrationBuilder.CreateTable(
                name: "Rule",
                columns: table => new
                {
                    RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    StopOnSuccess = table.Column<bool>(type: "bit", nullable: false),
                    OrdinanceSymbol = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    RuleSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rule", x => x.RuleId);
                    table.ForeignKey(
                        name: "FK_Rule_RuleSet_RuleSetId",
                        column: x => x.RuleSetId,
                        principalTable: "RuleSet",
                        principalColumn: "RuleSetId");
                });

            migrationBuilder.CreateTable(
                name: "PasswordResetOTP",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OTP = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpiryTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsUsed = table.Column<bool>(type: "bit", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordResetOTP", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PasswordResetOTP_UserMaster_UserId",
                        column: x => x.UserId,
                        principalTable: "UserMaster",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserPermission",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPermission", x => new { x.UserId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_UserPermission_Permission_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permission",
                        principalColumn: "PermissionId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserPermission_UserMaster_UserId",
                        column: x => x.UserId,
                        principalTable: "UserMaster",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamMaster",
                columns: table => new
                {
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExamType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RevaluationForExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Semester = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AcademicYearAYID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false),
                    CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamMaster", x => x.ExamId);
                    table.ForeignKey(
                        name: "FK_ExamMaster_AcademicYear_AcademicYearAYID",
                        column: x => x.AcademicYearAYID,
                        principalTable: "AcademicYear",
                        principalColumn: "AYID");
                    table.ForeignKey(
                        name: "FK_ExamMaster_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId");
                    table.ForeignKey(
                        name: "FK_ExamMaster_CourseMaster_CourseId",
                        column: x => x.CourseId,
                        principalTable: "CourseMaster",
                        principalColumn: "CourseId");
                });

            migrationBuilder.CreateTable(
                name: "StudentEligibility",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StdMstId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AYID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StudentId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SemesterId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Pattern = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsEligible = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentEligibility", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentEligibility_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId");
                    table.ForeignKey(
                        name: "FK_StudentEligibility_CourseMaster_CourseId",
                        column: x => x.CourseId,
                        principalTable: "CourseMaster",
                        principalColumn: "CourseId");
                    table.ForeignKey(
                        name: "FK_StudentEligibility_StudentMaster_StdMstId",
                        column: x => x.StdMstId,
                        principalTable: "StudentMaster",
                        principalColumn: "StdMstId");
                });

            migrationBuilder.CreateTable(
                name: "SubjectMaster",
                columns: table => new
                {
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubjectCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SemId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SemName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Pattern = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubjectMaster", x => x.SubjectId);
                    table.ForeignKey(
                        name: "FK_SubjectMaster_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId");
                    table.ForeignKey(
                        name: "FK_SubjectMaster_CourseMaster_CourseId",
                        column: x => x.CourseId,
                        principalTable: "CourseMaster",
                        principalColumn: "CourseId");
                });

            migrationBuilder.CreateTable(
                name: "RuleAction",
                columns: table => new
                {
                    ActionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CalculationMode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Param1Type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Param1Value = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Param2Type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Param2Value = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MaxLimit = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Expression = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    MaxTargetCount = table.Column<int>(type: "int", nullable: true),
                    Target = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleAction", x => x.ActionId);
                    table.ForeignKey(
                        name: "FK_RuleAction_Rule_RuleId",
                        column: x => x.RuleId,
                        principalTable: "Rule",
                        principalColumn: "RuleId");
                });

            migrationBuilder.CreateTable(
                name: "RuleCondition",
                columns: table => new
                {
                    ConditionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FactName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Operator = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Value = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleCondition", x => x.ConditionId);
                    table.ForeignKey(
                        name: "FK_RuleCondition_Rule_RuleId",
                        column: x => x.RuleId,
                        principalTable: "Rule",
                        principalColumn: "RuleId");
                });

            migrationBuilder.CreateTable(
                name: "MarksMaster",
                columns: table => new
                {
                    MarksId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StudentID = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    QuotaType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SeatNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SemesterId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Pattern = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OverallRemark = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    SGPI = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CGPI = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ResultRemark = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    HMCheck = table.Column<bool>(type: "bit", nullable: true),
                    Rank = table.Column<int>(type: "int", nullable: true),
                    StdMstId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AcademicYearAYID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarksMaster", x => x.MarksId);
                    table.ForeignKey(
                        name: "FK_MarksMaster_AcademicYear_AcademicYearAYID",
                        column: x => x.AcademicYearAYID,
                        principalTable: "AcademicYear",
                        principalColumn: "AYID");
                    table.ForeignKey(
                        name: "FK_MarksMaster_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId");
                    table.ForeignKey(
                        name: "FK_MarksMaster_ExamMaster_ExamId",
                        column: x => x.ExamId,
                        principalTable: "ExamMaster",
                        principalColumn: "ExamId");
                    table.ForeignKey(
                        name: "FK_MarksMaster_StudentMaster_StdMstId",
                        column: x => x.StdMstId,
                        principalTable: "StudentMaster",
                        principalColumn: "StdMstId");
                });

            migrationBuilder.CreateTable(
                name: "SubjectCreditMaster",
                columns: table => new
                {
                    CreditsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TotalCredits = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AYID = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PassingStrategy = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PassPercentage = table.Column<int>(type: "int", nullable: true),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AcademicYearAYID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubjectCreditMaster", x => x.CreditsId);
                    table.ForeignKey(
                        name: "FK_SubjectCreditMaster_AcademicYear_AcademicYearAYID",
                        column: x => x.AcademicYearAYID,
                        principalTable: "AcademicYear",
                        principalColumn: "AYID");
                    table.ForeignKey(
                        name: "FK_SubjectCreditMaster_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId");
                    table.ForeignKey(
                        name: "FK_SubjectCreditMaster_SubjectMaster_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "SubjectMaster",
                        principalColumn: "SubjectId");
                });

            migrationBuilder.CreateTable(
                name: "TimeTableMaster",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Time = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Date = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeTableMaster", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeTableMaster_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId");
                    table.ForeignKey(
                        name: "FK_TimeTableMaster_CourseMaster_CourseId",
                        column: x => x.CourseId,
                        principalTable: "CourseMaster",
                        principalColumn: "CourseId");
                    table.ForeignKey(
                        name: "FK_TimeTableMaster_ExamMaster_ExamId",
                        column: x => x.ExamId,
                        principalTable: "ExamMaster",
                        principalColumn: "ExamId");
                    table.ForeignKey(
                        name: "FK_TimeTableMaster_SubjectMaster_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "SubjectMaster",
                        principalColumn: "SubjectId");
                });

            migrationBuilder.CreateTable(
                name: "StudentMarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsCarryForward = table.Column<bool>(type: "bit", nullable: false),
                    Head = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RawMarks = table.Column<int>(type: "int", nullable: true),
                    Marks = table.Column<int>(type: "int", nullable: true),
                    Resolution = table.Column<int>(type: "int", nullable: true),
                    IsAbsent = table.Column<bool>(type: "bit", nullable: false),
                    Grace = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    MarksId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreditsId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentMarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentMarks_MarksMaster_MarksId",
                        column: x => x.MarksId,
                        principalTable: "MarksMaster",
                        principalColumn: "MarksId");
                    table.ForeignKey(
                        name: "FK_StudentMarks_SubjectCreditMaster_CreditsId",
                        column: x => x.CreditsId,
                        principalTable: "SubjectCreditMaster",
                        principalColumn: "CreditsId");
                    table.ForeignKey(
                        name: "FK_StudentMarks_SubjectMaster_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "SubjectMaster",
                        principalColumn: "SubjectId");
                });

            migrationBuilder.CreateTable(
                name: "StudentSubjectResult",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObtainedTotal = table.Column<int>(type: "int", nullable: false),
                    RawObtainedTotal = table.Column<int>(type: "int", nullable: false),
                    OutOfTotal = table.Column<int>(type: "int", nullable: false),
                    GraceApplied = table.Column<int>(type: "int", nullable: false),
                    GraceSymbol = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Grade = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    GradePoint = table.Column<int>(type: "int", nullable: false),
                    RawGradePoint = table.Column<int>(type: "int", nullable: false),
                    IsPassed = table.Column<bool>(type: "bit", nullable: false),
                    SubjectStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MarksId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditsId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentSubjectResult", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentSubjectResult_MarksMaster_MarksId",
                        column: x => x.MarksId,
                        principalTable: "MarksMaster",
                        principalColumn: "MarksId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudentSubjectResult_SubjectCreditMaster_CreditsId",
                        column: x => x.CreditsId,
                        principalTable: "SubjectCreditMaster",
                        principalColumn: "CreditsId");
                    table.ForeignKey(
                        name: "FK_StudentSubjectResult_SubjectMaster_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "SubjectMaster",
                        principalColumn: "SubjectId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubjectCredits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Head = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    HeadType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    HeadOutOf = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    HeadPass = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CreditsId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubjectCredits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubjectCredits_SubjectCreditMaster_CreditsId",
                        column: x => x.CreditsId,
                        principalTable: "SubjectCreditMaster",
                        principalColumn: "CreditsId");
                });

            migrationBuilder.CreateTable(
                name: "ResolutionMaster",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExamID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubjectCreditID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Resolution = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResolutionMaster", x => x.ID);
                    table.ForeignKey(
                        name: "FK_ResolutionMaster_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId");
                    table.ForeignKey(
                        name: "FK_ResolutionMaster_ExamMaster_ExamID",
                        column: x => x.ExamID,
                        principalTable: "ExamMaster",
                        principalColumn: "ExamId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ResolutionMaster_SubjectCreditMaster_CreditID",
                        column: x => x.CreditID,
                        principalTable: "SubjectCreditMaster",
                        principalColumn: "CreditsId");
                    table.ForeignKey(
                        name: "FK_ResolutionMaster_SubjectCredits_SubjectCreditID",
                        column: x => x.SubjectCreditID,
                        principalTable: "SubjectCredits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcademicYear_CollegeId",
                table: "AcademicYear",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_CollegeId",
                table: "AuditLog",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseMaster_CollegeId",
                table: "CourseMaster",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseMaster_StudentMasterStdMstId",
                table: "CourseMaster",
                column: "StudentMasterStdMstId");

            migrationBuilder.CreateIndex(
                name: "IX_DeclareResult_CollegeId",
                table: "DeclareResult",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamMaster_AcademicYearAYID",
                table: "ExamMaster",
                column: "AcademicYearAYID");

            migrationBuilder.CreateIndex(
                name: "IX_ExamMaster_CollegeId",
                table: "ExamMaster",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamMaster_CourseId",
                table: "ExamMaster",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_GraceLookup_CollegeId",
                table: "GraceLookup",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeMaster_CollegeId",
                table: "GradeMaster",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeThreshold_GradeMasterId",
                table: "GradeThreshold",
                column: "GradeMasterId");

            migrationBuilder.CreateIndex(
                name: "IX_MarksMaster_AcademicYearAYID",
                table: "MarksMaster",
                column: "AcademicYearAYID");

            migrationBuilder.CreateIndex(
                name: "IX_MarksMaster_CollegeId",
                table: "MarksMaster",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_MarksMaster_ExamId",
                table: "MarksMaster",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_MarksMaster_StdMstId",
                table: "MarksMaster",
                column: "StdMstId");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetOTP_UserId",
                table: "PasswordResetOTP",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PatternMaster_CollegeId",
                table: "PatternMaster",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_ResolutionMaster_CollegeId",
                table: "ResolutionMaster",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_ResolutionMaster_CreditID",
                table: "ResolutionMaster",
                column: "CreditID");

            migrationBuilder.CreateIndex(
                name: "IX_ResolutionMaster_ExamID_SubjectCreditID",
                table: "ResolutionMaster",
                columns: new[] { "ExamID", "SubjectCreditID" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ResolutionMaster_SubjectCreditID",
                table: "ResolutionMaster",
                column: "SubjectCreditID");

            migrationBuilder.CreateIndex(
                name: "IX_RoleMaster_CollegeId",
                table: "RoleMaster",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermission_PermissionId",
                table: "RolePermission",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_Rule_RuleSetId",
                table: "Rule",
                column: "RuleSetId");

            migrationBuilder.CreateIndex(
                name: "IX_RuleAction_RuleId",
                table: "RuleAction",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_RuleCondition_RuleId",
                table: "RuleCondition",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_RuleSet_CollegeId",
                table: "RuleSet",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_RuleSet_GradeMasterId",
                table: "RuleSet",
                column: "GradeMasterId");

            migrationBuilder.CreateIndex(
                name: "IX_RuleSet_PatternId",
                table: "RuleSet",
                column: "PatternId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentEligibility_CollegeId",
                table: "StudentEligibility",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentEligibility_CourseId",
                table: "StudentEligibility",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentEligibility_StdMstId",
                table: "StudentEligibility",
                column: "StdMstId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentMarks_CreditsId",
                table: "StudentMarks",
                column: "CreditsId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentMarks_MarksId",
                table: "StudentMarks",
                column: "MarksId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentMarks_SubjectId",
                table: "StudentMarks",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentMaster_CollegeId_StudentId",
                table: "StudentMaster",
                columns: new[] { "CollegeId", "StudentId" },
                unique: true,
                filter: "[CollegeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StudentsOverallResult_CollegeId",
                table: "StudentsOverallResult",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentsOverallResult_StdMstId",
                table: "StudentsOverallResult",
                column: "StdMstId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentSubjectResult_CreditsId",
                table: "StudentSubjectResult",
                column: "CreditsId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentSubjectResult_MarksId_SubjectId",
                table: "StudentSubjectResult",
                columns: new[] { "MarksId", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentSubjectResult_SubjectId",
                table: "StudentSubjectResult",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_SubjectCreditMaster_AcademicYearAYID",
                table: "SubjectCreditMaster",
                column: "AcademicYearAYID");

            migrationBuilder.CreateIndex(
                name: "IX_SubjectCreditMaster_CollegeId",
                table: "SubjectCreditMaster",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_SubjectCreditMaster_SubjectId",
                table: "SubjectCreditMaster",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_SubjectCredits_CreditsId",
                table: "SubjectCredits",
                column: "CreditsId");

            migrationBuilder.CreateIndex(
                name: "IX_SubjectMaster_CollegeId",
                table: "SubjectMaster",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_SubjectMaster_CourseId",
                table: "SubjectMaster",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeTableMaster_CollegeId",
                table: "TimeTableMaster",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeTableMaster_CourseId",
                table: "TimeTableMaster",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeTableMaster_ExamId",
                table: "TimeTableMaster",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeTableMaster_SubjectId",
                table: "TimeTableMaster",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMaster_CollegeId_Username",
                table: "UserMaster",
                columns: new[] { "CollegeId", "Username" },
                unique: true,
                filter: "[CollegeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserMaster_Email",
                table: "UserMaster",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserMaster_RoleId",
                table: "UserMaster",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPermission_PermissionId",
                table: "UserPermission",
                column: "PermissionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "DeclareResult");

            migrationBuilder.DropTable(
                name: "GraceLookup");

            migrationBuilder.DropTable(
                name: "GradeThreshold");

            migrationBuilder.DropTable(
                name: "PasswordResetOTP");

            migrationBuilder.DropTable(
                name: "ResolutionMaster");

            migrationBuilder.DropTable(
                name: "RolePermission");

            migrationBuilder.DropTable(
                name: "RuleAction");

            migrationBuilder.DropTable(
                name: "RuleCondition");

            migrationBuilder.DropTable(
                name: "StudentEligibility");

            migrationBuilder.DropTable(
                name: "StudentMarks");

            migrationBuilder.DropTable(
                name: "StudentsOverallResult");

            migrationBuilder.DropTable(
                name: "StudentSubjectResult");

            migrationBuilder.DropTable(
                name: "TimeTableMaster");

            migrationBuilder.DropTable(
                name: "UserPermission");

            migrationBuilder.DropTable(
                name: "SubjectCredits");

            migrationBuilder.DropTable(
                name: "Rule");

            migrationBuilder.DropTable(
                name: "MarksMaster");

            migrationBuilder.DropTable(
                name: "Permission");

            migrationBuilder.DropTable(
                name: "UserMaster");

            migrationBuilder.DropTable(
                name: "SubjectCreditMaster");

            migrationBuilder.DropTable(
                name: "RuleSet");

            migrationBuilder.DropTable(
                name: "ExamMaster");

            migrationBuilder.DropTable(
                name: "RoleMaster");

            migrationBuilder.DropTable(
                name: "SubjectMaster");

            migrationBuilder.DropTable(
                name: "GradeMaster");

            migrationBuilder.DropTable(
                name: "PatternMaster");

            migrationBuilder.DropTable(
                name: "AcademicYear");

            migrationBuilder.DropTable(
                name: "CourseMaster");

            migrationBuilder.DropTable(
                name: "StudentMaster");

            migrationBuilder.DropTable(
                name: "College");
        }
    }
}
