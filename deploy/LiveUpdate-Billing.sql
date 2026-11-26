/*
  GradeSphere LIVE update: staff billing (Billing screen, rate sets, staff claims by emailed link). Run ONCE in SSMS on
  the database GradeSphereApp, AFTER LiveUpdate-StaffLetters.sql, after stopping the API app pool and before copying the
  new API files. Safe to run again: every migration checks __EFMigrationsHistory and is skipped when already done.
  Adds 5 migrations (AddStaffBillingAndPriceSets, AddProjectOralAndMeRemuneration, AddBillingOtpAndProjectOralFields,
  RemoveLeaveFieldsFromStaffBillingClaim, AddBillingClaimIndexes): new billing tables only; nothing existing is changed
  or removed. Then adds the Billing screen to the permission catalog.
*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
IF DB_NAME() <> N'GradeSphereApp'
BEGIN
    RAISERROR(N'Wrong database: select GradeSphereApp and run again. Nothing was changed.', 16, 1);
    SET NOEXEC ON;
END
GO
IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = N'20261102041225_AddHallTicketInstructions')
BEGIN
    RAISERROR(N'Run LiveUpdate-StaffLetters.sql first (staff letters are not on this database yet). Nothing was changed.', 16, 1);
    SET NOEXEC ON;
END
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104050836_AddStaffBillingAndPriceSets'
)
BEGIN
    CREATE TABLE [BillingPriceSet] (
        [Id] uniqueidentifier NOT NULL,
        [CollegeId] uniqueidentifier NULL,
        [Name] nvarchar(150) NOT NULL,
        [Description] nvarchar(500) NULL,
        [IsDefault] bit NOT NULL,
        [ShowPricingToStaff] bit NOT NULL,
        [SeniorSupRate] decimal(10,2) NOT NULL,
        [JuniorSupRate] decimal(10,2) NOT NULL,
        [TheoryAssessmentRate] decimal(10,2) NOT NULL,
        [ModerationRate] decimal(10,2) NOT NULL,
        [TwRate] decimal(10,2) NOT NULL,
        [PracticalRate] decimal(10,2) NOT NULL,
        [OralRate] decimal(10,2) NOT NULL,
        [OwpRate] decimal(10,2) NOT NULL,
        [TtRate] decimal(10,2) NOT NULL,
        [TravelAllowanceRate] decimal(10,2) NOT NULL,
        [CapAllowanceRate] decimal(10,2) NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [DeletedAt] datetime2 NULL,
        CONSTRAINT [PK_BillingPriceSet] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104050836_AddStaffBillingAndPriceSets'
)
BEGIN
    CREATE TABLE [StaffBillingClaim] (
        [Id] uniqueidentifier NOT NULL,
        [CollegeId] uniqueidentifier NULL,
        [Token] nvarchar(100) NOT NULL,
        [DutyChartId] uniqueidentifier NULL,
        [StaffId] uniqueidentifier NULL,
        [PriceSetId] uniqueidentifier NULL,
        [AYID] uniqueidentifier NULL,
        [SessionName] nvarchar(150) NOT NULL,
        [StaffName] nvarchar(200) NOT NULL,
        [EmployeeCode] nvarchar(50) NOT NULL,
        [Department] nvarchar(100) NOT NULL,
        [Designation] nvarchar(100) NOT NULL,
        [Email] nvarchar(150) NOT NULL,
        [Phone] nvarchar(30) NULL,
        [RoleType] nvarchar(30) NOT NULL,
        [IsExternal] bit NOT NULL,
        [VisitingCollegeName] nvarchar(200) NULL,
        [City] nvarchar(100) NULL,
        [LeaveDays] int NOT NULL,
        [LeaveDatesAndReason] nvarchar(500) NULL,
        [AssignedSeniorSupDays] int NOT NULL,
        [AttendedSeniorSupDays] int NOT NULL,
        [SeniorSupRate] decimal(10,2) NOT NULL,
        [AssignedSeniorSupDates] nvarchar(200) NULL,
        [AssignedJuniorSupDays] int NOT NULL,
        [AttendedJuniorSupDays] int NOT NULL,
        [JuniorSupRate] decimal(10,2) NOT NULL,
        [AssignedJuniorSupDates] nvarchar(200) NULL,
        [TravelDays] int NOT NULL,
        [TravelRate] decimal(10,2) NOT NULL,
        [TravelDates] nvarchar(200) NULL,
        [CapDays] int NOT NULL,
        [CapRate] decimal(10,2) NOT NULL,
        [OtherAmount] decimal(10,2) NOT NULL,
        [OtherDescription] nvarchar(500) NULL,
        [BankAccNo] nvarchar(50) NOT NULL,
        [BankName] nvarchar(100) NOT NULL,
        [IfscCode] nvarchar(30) NOT NULL,
        [GrandTotal] decimal(10,2) NOT NULL,
        [AmountInWords] nvarchar(300) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [SavedBillNo] nvarchar(50) NULL,
        [SentAt] datetime2 NULL,
        [SubmittedAt] datetime2 NULL,
        [ApprovedAt] datetime2 NULL,
        [ApprovedBy] nvarchar(100) NULL,
        [Remarks] nvarchar(500) NULL,
        [AdminRemarks] nvarchar(500) NULL,
        [CreatedBy] uniqueidentifier NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [DeletedAt] datetime2 NULL,
        CONSTRAINT [PK_StaffBillingClaim] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104050836_AddStaffBillingAndPriceSets'
)
BEGIN
    CREATE TABLE [StaffBillItem] (
        [Id] uniqueidentifier NOT NULL,
        [CollegeId] uniqueidentifier NULL,
        [ClaimId] uniqueidentifier NOT NULL,
        [SubjectName] nvarchar(150) NOT NULL,
        [Semester] nvarchar(30) NOT NULL,
        [TheoryCount] int NOT NULL,
        [TheoryRate] decimal(10,2) NOT NULL,
        [TwCount] int NOT NULL,
        [TwRate] decimal(10,2) NOT NULL,
        [PracticalCount] int NOT NULL,
        [PracticalRate] decimal(10,2) NOT NULL,
        [OralCount] int NOT NULL,
        [OralRate] decimal(10,2) NOT NULL,
        [OwpCount] int NOT NULL,
        [OwpRate] decimal(10,2) NOT NULL,
        [TtCount] int NOT NULL,
        [TtRate] decimal(10,2) NOT NULL,
        [ItemSubtotal] decimal(10,2) NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [DeletedAt] datetime2 NULL,
        CONSTRAINT [PK_StaffBillItem] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StaffBillItem_StaffBillingClaim_ClaimId] FOREIGN KEY ([ClaimId]) REFERENCES [StaffBillingClaim] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104050836_AddStaffBillingAndPriceSets'
)
BEGIN
    CREATE INDEX [IX_StaffBillItem_ClaimId] ON [StaffBillItem] ([ClaimId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104050836_AddStaffBillingAndPriceSets'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261104050836_AddStaffBillingAndPriceSets', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104055945_AddProjectOralAndMeRemuneration'
)
BEGIN
    ALTER TABLE [StaffBillItem] ADD [MeCount] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104055945_AddProjectOralAndMeRemuneration'
)
BEGIN
    ALTER TABLE [StaffBillItem] ADD [MeRate] decimal(10,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104055945_AddProjectOralAndMeRemuneration'
)
BEGIN
    ALTER TABLE [StaffBillItem] ADD [ProjectOralCount] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104055945_AddProjectOralAndMeRemuneration'
)
BEGIN
    ALTER TABLE [StaffBillItem] ADD [ProjectOralRate] decimal(10,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104055945_AddProjectOralAndMeRemuneration'
)
BEGIN
    ALTER TABLE [BillingPriceSet] ADD [MeRate] decimal(10,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104055945_AddProjectOralAndMeRemuneration'
)
BEGIN
    ALTER TABLE [BillingPriceSet] ADD [ProjectOralRate] decimal(10,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104055945_AddProjectOralAndMeRemuneration'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261104055945_AddProjectOralAndMeRemuneration', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105051440_AddBillingOtpAndProjectOralFields'
)
BEGIN
    ALTER TABLE [StaffBillingClaim] ADD [OtpCodeHash] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105051440_AddBillingOtpAndProjectOralFields'
)
BEGIN
    ALTER TABLE [StaffBillingClaim] ADD [OtpExpiresAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105051440_AddBillingOtpAndProjectOralFields'
)
BEGIN
    ALTER TABLE [StaffBillingClaim] ADD [OtpSentAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105051440_AddBillingOtpAndProjectOralFields'
)
BEGIN
    ALTER TABLE [StaffBillingClaim] ADD [OtpAttempts] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105051440_AddBillingOtpAndProjectOralFields'
)
BEGIN
    ALTER TABLE [StaffBillingClaim] ADD [OtpVerifiedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105051440_AddBillingOtpAndProjectOralFields'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_StaffBillingClaim_Token] ON [StaffBillingClaim] ([Token]) WHERE [Token] <> N''''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105051440_AddBillingOtpAndProjectOralFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261105051440_AddBillingOtpAndProjectOralFields', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109070000_RemoveLeaveFieldsFromStaffBillingClaim'
)
BEGIN
    DECLARE @var sysname;
    SELECT @var = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[StaffBillingClaim]') AND [c].[name] = N'LeaveDays');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [StaffBillingClaim] DROP CONSTRAINT [' + @var + '];');
    ALTER TABLE [StaffBillingClaim] DROP COLUMN [LeaveDays];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109070000_RemoveLeaveFieldsFromStaffBillingClaim'
)
BEGIN
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[StaffBillingClaim]') AND [c].[name] = N'LeaveDatesAndReason');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [StaffBillingClaim] DROP CONSTRAINT [' + @var1 + '];');
    ALTER TABLE [StaffBillingClaim] DROP COLUMN [LeaveDatesAndReason];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109070000_RemoveLeaveFieldsFromStaffBillingClaim'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261109070000_RemoveLeaveFieldsFromStaffBillingClaim', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261126053937_AddBillingClaimIndexes'
)
BEGIN
    CREATE INDEX [IX_StaffBillingClaim_DutyChartId_StaffId] ON [StaffBillingClaim] ([DutyChartId], [StaffId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261126053937_AddBillingClaimIndexes'
)
BEGIN
    DROP INDEX [IX_StaffBillingClaim_Token] ON [StaffBillingClaim];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261126053937_AddBillingClaimIndexes'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_StaffBillingClaim_Token] ON [StaffBillingClaim] ([Token]) WHERE [Token] IS NOT NULL AND [Token] <> ''''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261126053937_AddBillingClaimIndexes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261126053937_AddBillingClaimIndexes', N'9.0.2');
END;

COMMIT;
GO



GO
-- The Billing screen in the permission catalog (so college admins can tick it for staff roles on Role Master).
SET QUOTED_IDENTIFIER ON;
INSERT Permission (PermissionId, PermissionFormName, PermissionModuleName, CreatedAt, IsDeleted)
SELECT NEWID(), N'Billing', N'Staff Admin', SYSUTCDATETIME(), 0
WHERE NOT EXISTS (SELECT 1 FROM Permission p WHERE p.IsDeleted = 0 AND p.PermissionFormName IN (N'Billing', N'Exam Billing', N'Remuneration'));
SELECT PermissionModuleName, PermissionFormName FROM Permission WHERE IsDeleted = 0 AND PermissionModuleName = N'Staff Admin' ORDER BY PermissionFormName;
SELECT MigrationId FROM __EFMigrationsHistory WHERE MigrationId >= N'20261104' ORDER BY MigrationId;
GO
SET NOEXEC OFF;
GO