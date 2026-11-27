using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddExamApplicationsAndPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Pattern",
                table: "TaskBatch",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExamApplication",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StdMstId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SemesterId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Pattern = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TaskLinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AmountDue = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    FeeBreakdownJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReceiptNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaidVia = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    PaymentReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AssignError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PaidTwice = table.Column<bool>(type: "bit", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamApplication", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamApplication_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamApplication_ExamMaster_TargetExamId",
                        column: x => x.TargetExamId,
                        principalTable: "ExamMaster",
                        principalColumn: "ExamId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamApplication_StudentMaster_StdMstId",
                        column: x => x.StdMstId,
                        principalTable: "StudentMaster",
                        principalColumn: "StdMstId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamFeeSetup",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    OpensAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosesAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PerSubjectFee = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    MaxSubjectFee = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    FixedFee = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    FixedFeeLabel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LateFeeFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LateFee = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamFeeSetup", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamFeeSetup_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamFeeSetup_ExamMaster_ExamId",
                        column: x => x.ExamId,
                        principalTable: "ExamMaster",
                        principalColumn: "ExamId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentAccount",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MerchantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SecretsProtected = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IsTestMode = table.Column<bool>(type: "bit", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAccount", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentAccount_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptCounter",
                columns: table => new
                {
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Series = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    LastNo = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptCounter", x => new { x.CollegeId, x.Series });
                    table.ForeignKey(
                        name: "FK_ReceiptCounter_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamApplicationSubject",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Heads = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamApplicationSubject", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamApplicationSubject_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamApplicationSubject_ExamApplication_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "ExamApplication",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamFeeCategoryRate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FeeSetupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsExempt = table.Column<bool>(type: "bit", nullable: false),
                    PerSubjectFee = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    FixedFee = table.Column<decimal>(type: "decimal(10,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamFeeCategoryRate", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamFeeCategoryRate_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamFeeCategoryRate_ExamFeeSetup_FeeSetupId",
                        column: x => x.FeeSetupId,
                        principalTable: "ExamFeeSetup",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentOrder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    PaymentAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsTestMode = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    GatewayTxnId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Mode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Bank = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    GatewayStatus = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReturnBaseUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastCheckedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextCheckAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CheckCount = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentOrder", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentOrder_College_CollegeId",
                        column: x => x.CollegeId,
                        principalTable: "College",
                        principalColumn: "CollegeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentOrder_ExamApplication_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "ExamApplication",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentOrder_PaymentAccount_PaymentAccountId",
                        column: x => x.PaymentAccountId,
                        principalTable: "PaymentAccount",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "TaskType",
                keyColumn: "Code",
                keyValue: "ATKT_PAYMENT",
                columns: new[] { "ExpiryRule", "Name" },
                values: new object[] { "DIE_ON_DEADLINE", "Apply for exam" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamApplication_CollegeId_TargetExamId_Status",
                table: "ExamApplication",
                columns: new[] { "CollegeId", "TargetExamId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamApplication_StdMstId_TargetExamId",
                table: "ExamApplication",
                columns: new[] { "StdMstId", "TargetExamId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Status] <> 'Cancelled' AND [Status] <> 'Refunded'");

            migrationBuilder.CreateIndex(
                name: "IX_ExamApplication_TargetExamId",
                table: "ExamApplication",
                column: "TargetExamId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamApplicationSubject_ApplicationId_SubjectId",
                table: "ExamApplicationSubject",
                columns: new[] { "ApplicationId", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamApplicationSubject_CollegeId",
                table: "ExamApplicationSubject",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamFeeCategoryRate_CollegeId",
                table: "ExamFeeCategoryRate",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamFeeCategoryRate_FeeSetupId_Category",
                table: "ExamFeeCategoryRate",
                columns: new[] { "FeeSetupId", "Category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamFeeSetup_CollegeId_ExamId",
                table: "ExamFeeSetup",
                columns: new[] { "CollegeId", "ExamId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ExamFeeSetup_ExamId",
                table: "ExamFeeSetup",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAccount_CollegeId",
                table: "PaymentAccount",
                column: "CollegeId",
                unique: true,
                filter: "[CollegeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrder_ApplicationId",
                table: "PaymentOrder",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrder_CollegeId",
                table: "PaymentOrder",
                column: "CollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrder_OrderNo",
                table: "PaymentOrder",
                column: "OrderNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrder_PaymentAccountId",
                table: "PaymentOrder",
                column: "PaymentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrder_Status_NextCheckAt",
                table: "PaymentOrder",
                columns: new[] { "Status", "NextCheckAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExamApplicationSubject");

            migrationBuilder.DropTable(
                name: "ExamFeeCategoryRate");

            migrationBuilder.DropTable(
                name: "PaymentOrder");

            migrationBuilder.DropTable(
                name: "ReceiptCounter");

            migrationBuilder.DropTable(
                name: "ExamFeeSetup");

            migrationBuilder.DropTable(
                name: "ExamApplication");

            migrationBuilder.DropTable(
                name: "PaymentAccount");

            migrationBuilder.DropColumn(
                name: "Pattern",
                table: "TaskBatch");

            migrationBuilder.UpdateData(
                table: "TaskType",
                keyColumn: "Code",
                keyValue: "ATKT_PAYMENT",
                columns: new[] { "ExpiryRule", "Name" },
                values: new object[] { "DIE_ON_PAYMENT", "Pay ATKT exam fee" });
        }
    }
}
