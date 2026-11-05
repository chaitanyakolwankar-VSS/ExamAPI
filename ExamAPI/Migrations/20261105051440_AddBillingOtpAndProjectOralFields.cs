using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingOtpAndProjectOralFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingPriceSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    ShowPricingToStaff = table.Column<bool>(type: "bit", nullable: false),
                    SeniorSupRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    JuniorSupRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    TheoryAssessmentRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    ModerationRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    TwRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    PracticalRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    OralRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    ProjectOralRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    MeRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    OwpRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    TtRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    TravelAllowanceRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    CapAllowanceRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPriceSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StaffBillingClaim",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Token = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DutyChartId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PriceSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AYID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    StaffName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Department = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Designation = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    RoleType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsExternal = table.Column<bool>(type: "bit", nullable: false),
                    VisitingCollegeName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LeaveDays = table.Column<int>(type: "int", nullable: false),
                    LeaveDatesAndReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AssignedSeniorSupDays = table.Column<int>(type: "int", nullable: false),
                    AttendedSeniorSupDays = table.Column<int>(type: "int", nullable: false),
                    SeniorSupRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    AssignedSeniorSupDates = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AssignedJuniorSupDays = table.Column<int>(type: "int", nullable: false),
                    AttendedJuniorSupDays = table.Column<int>(type: "int", nullable: false),
                    JuniorSupRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    AssignedJuniorSupDates = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TravelDays = table.Column<int>(type: "int", nullable: false),
                    TravelRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    TravelDates = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CapDays = table.Column<int>(type: "int", nullable: false),
                    CapRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    OtherAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    OtherDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BankAccNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IfscCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    AmountInWords = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SavedBillNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AdminRemarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OtpCodeHash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OtpExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OtpSentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OtpAttempts = table.Column<int>(type: "int", nullable: false),
                    OtpVerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffBillingClaim", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StaffBillItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Semester = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TheoryCount = table.Column<int>(type: "int", nullable: false),
                    TheoryRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    TwCount = table.Column<int>(type: "int", nullable: false),
                    TwRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    PracticalCount = table.Column<int>(type: "int", nullable: false),
                    PracticalRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    OralCount = table.Column<int>(type: "int", nullable: false),
                    OralRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    ProjectOralCount = table.Column<int>(type: "int", nullable: false),
                    ProjectOralRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    MeCount = table.Column<int>(type: "int", nullable: false),
                    MeRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    OwpCount = table.Column<int>(type: "int", nullable: false),
                    OwpRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    TtCount = table.Column<int>(type: "int", nullable: false),
                    TtRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    ItemSubtotal = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffBillItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffBillItem_StaffBillingClaim_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "StaffBillingClaim",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffBillItem_ClaimId",
                table: "StaffBillItem",
                column: "ClaimId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingPriceSet");

            migrationBuilder.DropTable(
                name: "StaffBillItem");

            migrationBuilder.DropTable(
                name: "StaffBillingClaim");
        }
    }
}
