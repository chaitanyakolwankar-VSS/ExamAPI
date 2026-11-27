/*
  GradeSphere LIVE update: student exam applications (ATKT / Re-Exam / Revaluation: fees, apply and pay by link,
  automatic registration, fee report) and payment gateway accounts. Run ONCE in SSMS on the database GradeSphereApp,
  AFTER LiveUpdate-Billing.sql, after stopping the API app pool and before copying the new API files. Safe to run
  again: the migration checks __EFMigrationsHistory and is skipped when already done.
  Adds 2 migrations (AddExamApplicationsAndPayments, AddExamFormWording): 7 new tables (ExamFeeSetup, ExamFeeCategoryRate, ExamApplication,
  ExamApplicationSubject, PaymentOrder, PaymentAccount, ReceiptCounter), new nullable columns TaskBatch.Pattern and
  CollegeSettings.ExamFormDeclaration / ExamFormNote (the application form wording; empty = built-in text) and
  ExamApplication.DeclaredAt, and
  renames the reserved student-link type ATKT_PAYMENT to "Apply for exam" (open until the closing date). Nothing
  existing is removed. Then adds the Exam Fees and Exam Applications screens to the permission catalog.
  Online payment stays off until a gateway account is saved on the Platform page (T-79).
*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
IF DB_NAME() <> N'GradeSphereApp'
BEGIN
    RAISERROR(N'Wrong database: select GradeSphereApp and run again. Nothing was changed.', 16, 1);
    SET NOEXEC ON;
END
GO
IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = N'20261126053937_AddBillingClaimIndexes')
BEGIN
    RAISERROR(N'Run LiveUpdate-Billing.sql first (billing is not on this database yet). Nothing was changed.', 16, 1);
    SET NOEXEC ON;
END
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    ALTER TABLE [TaskBatch] ADD [Pattern] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    CREATE TABLE [ExamApplication] (
        [Id] uniqueidentifier NOT NULL,
        [CollegeId] uniqueidentifier NULL,
        [StdMstId] uniqueidentifier NOT NULL,
        [TargetExamId] uniqueidentifier NOT NULL,
        [SourceExamId] uniqueidentifier NULL,
        [SemesterId] nvarchar(50) NULL,
        [Pattern] nvarchar(50) NULL,
        [TaskLinkId] uniqueidentifier NULL,
        [Kind] nvarchar(10) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [AmountDue] decimal(10,2) NOT NULL,
        [FeeBreakdownJson] nvarchar(2000) NULL,
        [ReceiptNo] nvarchar(40) NULL,
        [PaidAt] datetime2 NULL,
        [PaidVia] nvarchar(10) NULL,
        [PaymentReference] nvarchar(100) NULL,
        [AssignedAt] datetime2 NULL,
        [AssignError] nvarchar(1000) NULL,
        [Note] nvarchar(1000) NULL,
        [PaidTwice] bit NOT NULL,
        [ConcurrencyStamp] uniqueidentifier NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [DeletedAt] datetime2 NULL,
        CONSTRAINT [PK_ExamApplication] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ExamApplication_College_CollegeId] FOREIGN KEY ([CollegeId]) REFERENCES [College] ([CollegeId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ExamApplication_ExamMaster_TargetExamId] FOREIGN KEY ([TargetExamId]) REFERENCES [ExamMaster] ([ExamId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ExamApplication_StudentMaster_StdMstId] FOREIGN KEY ([StdMstId]) REFERENCES [StudentMaster] ([StdMstId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    CREATE TABLE [ExamFeeSetup] (
        [Id] uniqueidentifier NOT NULL,
        [CollegeId] uniqueidentifier NULL,
        [ExamId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(10) NOT NULL,
        [OpensAt] datetime2 NULL,
        [ClosesAt] datetime2 NOT NULL,
        [PerSubjectFee] decimal(10,2) NOT NULL,
        [MaxSubjectFee] decimal(10,2) NULL,
        [FixedFee] decimal(10,2) NOT NULL,
        [FixedFeeLabel] nvarchar(100) NULL,
        [LateFeeFrom] datetime2 NULL,
        [LateFee] decimal(10,2) NOT NULL,
        [Notes] nvarchar(1000) NULL,
        [CreatedBy] uniqueidentifier NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [DeletedAt] datetime2 NULL,
        CONSTRAINT [PK_ExamFeeSetup] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ExamFeeSetup_College_CollegeId] FOREIGN KEY ([CollegeId]) REFERENCES [College] ([CollegeId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ExamFeeSetup_ExamMaster_ExamId] FOREIGN KEY ([ExamId]) REFERENCES [ExamMaster] ([ExamId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    CREATE TABLE [PaymentAccount] (
        [Id] uniqueidentifier NOT NULL,
        [CollegeId] uniqueidentifier NULL,
        [Provider] nvarchar(20) NOT NULL,
        [MerchantId] nvarchar(100) NULL,
        [SecretsProtected] nvarchar(4000) NULL,
        [IsTestMode] bit NOT NULL,
        [IsEnabled] bit NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_PaymentAccount] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PaymentAccount_College_CollegeId] FOREIGN KEY ([CollegeId]) REFERENCES [College] ([CollegeId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    CREATE TABLE [ReceiptCounter] (
        [CollegeId] uniqueidentifier NOT NULL,
        [Series] nvarchar(40) NOT NULL,
        [LastNo] int NOT NULL,
        CONSTRAINT [PK_ReceiptCounter] PRIMARY KEY ([CollegeId], [Series]),
        CONSTRAINT [FK_ReceiptCounter_College_CollegeId] FOREIGN KEY ([CollegeId]) REFERENCES [College] ([CollegeId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    CREATE TABLE [ExamApplicationSubject] (
        [Id] uniqueidentifier NOT NULL,
        [CollegeId] uniqueidentifier NULL,
        [ApplicationId] uniqueidentifier NOT NULL,
        [SubjectId] uniqueidentifier NOT NULL,
        [Heads] nvarchar(200) NULL,
        CONSTRAINT [PK_ExamApplicationSubject] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ExamApplicationSubject_College_CollegeId] FOREIGN KEY ([CollegeId]) REFERENCES [College] ([CollegeId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ExamApplicationSubject_ExamApplication_ApplicationId] FOREIGN KEY ([ApplicationId]) REFERENCES [ExamApplication] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    CREATE TABLE [ExamFeeCategoryRate] (
        [Id] uniqueidentifier NOT NULL,
        [CollegeId] uniqueidentifier NULL,
        [FeeSetupId] uniqueidentifier NOT NULL,
        [Category] nvarchar(50) NOT NULL,
        [IsExempt] bit NOT NULL,
        [PerSubjectFee] decimal(10,2) NULL,
        [FixedFee] decimal(10,2) NULL,
        CONSTRAINT [PK_ExamFeeCategoryRate] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ExamFeeCategoryRate_College_CollegeId] FOREIGN KEY ([CollegeId]) REFERENCES [College] ([CollegeId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ExamFeeCategoryRate_ExamFeeSetup_FeeSetupId] FOREIGN KEY ([FeeSetupId]) REFERENCES [ExamFeeSetup] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    CREATE TABLE [PaymentOrder] (
        [Id] uniqueidentifier NOT NULL,
        [CollegeId] uniqueidentifier NULL,
        [ApplicationId] uniqueidentifier NOT NULL,
        [OrderNo] nvarchar(40) NOT NULL,
        [Amount] decimal(10,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [PaymentAccountId] uniqueidentifier NULL,
        [Provider] nvarchar(20) NOT NULL,
        [IsTestMode] bit NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [GatewayTxnId] nvarchar(100) NULL,
        [Mode] nvarchar(50) NULL,
        [Bank] nvarchar(100) NULL,
        [GatewayStatus] nvarchar(100) NULL,
        [Message] nvarchar(500) NULL,
        [ReturnBaseUrl] nvarchar(300) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ConfirmedAt] datetime2 NULL,
        [LastCheckedAt] datetime2 NULL,
        [NextCheckAt] datetime2 NULL,
        [CheckCount] int NOT NULL,
        [ConcurrencyStamp] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_PaymentOrder] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PaymentOrder_College_CollegeId] FOREIGN KEY ([CollegeId]) REFERENCES [College] ([CollegeId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PaymentOrder_ExamApplication_ApplicationId] FOREIGN KEY ([ApplicationId]) REFERENCES [ExamApplication] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PaymentOrder_PaymentAccount_PaymentAccountId] FOREIGN KEY ([PaymentAccountId]) REFERENCES [PaymentAccount] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    EXEC(N'UPDATE [TaskType] SET [ExpiryRule] = N''DIE_ON_DEADLINE'', [Name] = N''Apply for exam''
    WHERE [Code] = N''ATKT_PAYMENT'';
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    CREATE INDEX [IX_ExamApplication_CollegeId_TargetExamId_Status] ON [ExamApplication] ([CollegeId], [TargetExamId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ExamApplication_StdMstId_TargetExamId] ON [ExamApplication] ([StdMstId], [TargetExamId]) WHERE [IsDeleted] = 0 AND [Status] <> ''Cancelled'' AND [Status] <> ''Refunded''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    CREATE INDEX [IX_ExamApplication_TargetExamId] ON [ExamApplication] ([TargetExamId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ExamApplicationSubject_ApplicationId_SubjectId] ON [ExamApplicationSubject] ([ApplicationId], [SubjectId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    CREATE INDEX [IX_ExamApplicationSubject_CollegeId] ON [ExamApplicationSubject] ([CollegeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    CREATE INDEX [IX_ExamFeeCategoryRate_CollegeId] ON [ExamFeeCategoryRate] ([CollegeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ExamFeeCategoryRate_FeeSetupId_Category] ON [ExamFeeCategoryRate] ([FeeSetupId], [Category]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ExamFeeSetup_CollegeId_ExamId] ON [ExamFeeSetup] ([CollegeId], [ExamId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    CREATE INDEX [IX_ExamFeeSetup_ExamId] ON [ExamFeeSetup] ([ExamId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_PaymentAccount_CollegeId] ON [PaymentAccount] ([CollegeId]) WHERE [CollegeId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    CREATE INDEX [IX_PaymentOrder_ApplicationId] ON [PaymentOrder] ([ApplicationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    CREATE INDEX [IX_PaymentOrder_CollegeId] ON [PaymentOrder] ([CollegeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PaymentOrder_OrderNo] ON [PaymentOrder] ([OrderNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    CREATE INDEX [IX_PaymentOrder_PaymentAccountId] ON [PaymentOrder] ([PaymentAccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    CREATE INDEX [IX_PaymentOrder_Status_NextCheckAt] ON [PaymentOrder] ([Status], [NextCheckAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127085711_AddExamApplicationsAndPayments'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261127085711_AddExamApplicationsAndPayments', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127110620_AddExamFormWording'
)
BEGIN
    ALTER TABLE [ExamApplication] ADD [DeclaredAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127110620_AddExamFormWording'
)
BEGIN
    ALTER TABLE [CollegeSettings] ADD [ExamFormDeclaration] nvarchar(2000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127110620_AddExamFormWording'
)
BEGIN
    ALTER TABLE [CollegeSettings] ADD [ExamFormNote] nvarchar(1000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127110620_AddExamFormWording'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261127110620_AddExamFormWording', N'9.0.2');
END;

COMMIT;
GO


GO
-- The two new screens in the permission catalog (so college admins can tick them for staff roles on Role Master).
SET QUOTED_IDENTIFIER ON;
INSERT Permission (PermissionId, PermissionFormName, PermissionModuleName, CreatedAt, IsDeleted)
SELECT NEWID(), N'Exam Fees', N'Conduct Exam', SYSUTCDATETIME(), 0
WHERE NOT EXISTS (SELECT 1 FROM Permission p WHERE p.IsDeleted = 0 AND p.PermissionFormName IN (N'Exam Fees', N'Exam Fee', N'ATKT Fees', N'Revaluation Fees'));
INSERT Permission (PermissionId, PermissionFormName, PermissionModuleName, CreatedAt, IsDeleted)
SELECT NEWID(), N'Exam Applications', N'Conduct Exam', SYSUTCDATETIME(), 0
WHERE NOT EXISTS (SELECT 1 FROM Permission p WHERE p.IsDeleted = 0 AND p.PermissionFormName IN (N'Exam Applications', N'ATKT Applications', N'Exam Fee Report', N'Fee Report'));
SELECT PermissionModuleName, PermissionFormName FROM Permission WHERE IsDeleted = 0 AND PermissionModuleName = N'Conduct Exam' ORDER BY PermissionFormName;
SELECT MigrationId FROM __EFMigrationsHistory WHERE MigrationId >= N'20261126' ORDER BY MigrationId;
GO
SET NOEXEC OFF;
GO
