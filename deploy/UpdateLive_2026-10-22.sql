-- =======================================================================================
-- UpdateLive_2026-10-22.sql
-- One idempotent script for every migration added since the live release of 2026-10-07
-- (generated with dotnet ef migrations script 20261005045703_InitialSchema --idempotent):
--   AddStudentMotherName, AddStudentType, AddSubjectSortOrder, AddStudentAccess, AddEmailAccounts,
--   AddTaskLinkTracking, AddAbcIdAndCollegeSeal.
-- Each part runs only if its __EFMigrationsHistory row is absent, so parts the live DB already has are skipped.
-- It changes the schema and adds reference rows (task types); existing data is kept. The only updates to
-- existing rows: subject sort order filled in, existing student emails trimmed and lower-cased (duplicates flagged).
--
-- Run on the live GradeSphereApp database only, after a backup, with: sqlcmd ... -I -i UpdateLive_2026-10-22.sql
-- (or open in SSMS and execute). Replaces running AddSubjectSortOrder / AddStudentAccess / AddEmailAccounts /
-- AddTaskLinkTracking / AddAbcIdAndCollegeSeal one by one.
-- =======================================================================================
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012062712_AddStudentMotherName'
)
BEGIN
    ALTER TABLE [StudentMaster] ADD [MotherName] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012062712_AddStudentMotherName'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261012062712_AddStudentMotherName', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012094252_AddStudentType'
)
BEGIN
    ALTER TABLE [StudentMaster] ADD [StudentType] nvarchar(1) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012094252_AddStudentType'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261012094252_AddStudentType', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014045830_AddSubjectSortOrder'
)
BEGIN
    ALTER TABLE [SubjectMaster] ADD [SortOrder] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014045830_AddSubjectSortOrder'
)
BEGIN
    EXEC(N'
    UPDATE s SET s.SortOrder = n.rn
    FROM SubjectMaster s
    JOIN (
        SELECT SubjectId,
               ROW_NUMBER() OVER (PARTITION BY CollegeId, CourseId, Pattern, SemId
                                  ORDER BY CreatedAt, SubjectCode) AS rn
        FROM SubjectMaster
        WHERE IsDeleted = 0
    ) n ON n.SubjectId = s.SubjectId
    WHERE s.SortOrder IS NULL;')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014045830_AddSubjectSortOrder'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261014045830_AddSubjectSortOrder', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    ALTER TABLE [StudentMaster] ADD [EmailFlagReason] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    ALTER TABLE [StudentMaster] ADD [EmailStatus] nvarchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    ALTER TABLE [StudentMaster] ADD [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    ALTER TABLE [AuditLog] ADD [Details] nvarchar(1000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    ALTER TABLE [AuditLog] ADD [Event] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    CREATE TABLE [CollegeSettings] (
        [Id] uniqueidentifier NOT NULL,
        [CollegeId] uniqueidentifier NULL,
        [AllowedEmailDomains] nvarchar(500) NULL,
        [ExamOfficeEmail] nvarchar(255) NULL,
        [EmailSenderKey] nvarchar(50) NULL,
        [DailyEmailLimit] int NOT NULL DEFAULT 2000,
        [CreatedBy] uniqueidentifier NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [DeletedAt] datetime2 NULL,
        CONSTRAINT [PK_CollegeSettings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CollegeSettings_College_CollegeId] FOREIGN KEY ([CollegeId]) REFERENCES [College] ([CollegeId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    CREATE TABLE [EmailQueue] (
        [Id] uniqueidentifier NOT NULL,
        [CollegeId] uniqueidentifier NULL,
        [Kind] nvarchar(20) NOT NULL,
        [RefId] uniqueidentifier NULL,
        [ToAddress] nvarchar(255) NOT NULL,
        [Subject] nvarchar(300) NULL,
        [HtmlBody] nvarchar(max) NULL,
        [TextBody] nvarchar(max) NULL,
        [Priority] int NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [Attempts] int NOT NULL,
        [LastError] nvarchar(1000) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [NextAttemptAt] datetime2 NOT NULL,
        [LockedUntil] datetime2 NULL,
        [SentAt] datetime2 NULL,
        CONSTRAINT [PK_EmailQueue] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmailQueue_College_CollegeId] FOREIGN KEY ([CollegeId]) REFERENCES [College] ([CollegeId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    CREATE TABLE [TaskType] (
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [ExpiryRule] nvarchar(20) NOT NULL,
        [RequiresOtp] bit NOT NULL,
        CONSTRAINT [PK_TaskType] PRIMARY KEY ([Code])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    CREATE TABLE [TaskBatch] (
        [Id] uniqueidentifier NOT NULL,
        [CollegeId] uniqueidentifier NULL,
        [TaskTypeCode] nvarchar(30) NOT NULL,
        [Title] nvarchar(200) NULL,
        [Deadline] datetime2 NULL,
        [RefId] uniqueidentifier NULL,
        [CourseId] uniqueidentifier NULL,
        [SemesterId] nvarchar(50) NULL,
        [AYID] uniqueidentifier NULL,
        [CreatedBy] uniqueidentifier NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [DeletedAt] datetime2 NULL,
        CONSTRAINT [PK_TaskBatch] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TaskBatch_College_CollegeId] FOREIGN KEY ([CollegeId]) REFERENCES [College] ([CollegeId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_TaskBatch_TaskType_TaskTypeCode] FOREIGN KEY ([TaskTypeCode]) REFERENCES [TaskType] ([Code]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    CREATE TABLE [TaskLink] (
        [Id] uniqueidentifier NOT NULL,
        [CollegeId] uniqueidentifier NULL,
        [TaskTypeCode] nvarchar(30) NOT NULL,
        [BatchId] uniqueidentifier NULL,
        [StdMstId] uniqueidentifier NOT NULL,
        [RefId] uniqueidentifier NULL,
        [TokenHash] binary(32) NULL,
        [Status] nvarchar(20) NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [SentAt] datetime2 NULL,
        [UsedAt] datetime2 NULL,
        [RevokedAt] datetime2 NULL,
        [CreatedBy] uniqueidentifier NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [DeletedAt] datetime2 NULL,
        CONSTRAINT [PK_TaskLink] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TaskLink_College_CollegeId] FOREIGN KEY ([CollegeId]) REFERENCES [College] ([CollegeId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_TaskLink_StudentMaster_StdMstId] FOREIGN KEY ([StdMstId]) REFERENCES [StudentMaster] ([StdMstId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_TaskLink_TaskBatch_BatchId] FOREIGN KEY ([BatchId]) REFERENCES [TaskBatch] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_TaskLink_TaskType_TaskTypeCode] FOREIGN KEY ([TaskTypeCode]) REFERENCES [TaskType] ([Code]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    CREATE TABLE [OtpChallenge] (
        [Id] uniqueidentifier NOT NULL,
        [CollegeId] uniqueidentifier NULL,
        [TaskLinkId] uniqueidentifier NOT NULL,
        [CodeHash] binary(32) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [AttemptCount] int NOT NULL,
        [UsedAt] datetime2 NULL,
        CONSTRAINT [PK_OtpChallenge] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OtpChallenge_College_CollegeId] FOREIGN KEY ([CollegeId]) REFERENCES [College] ([CollegeId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OtpChallenge_TaskLink_TaskLinkId] FOREIGN KEY ([TaskLinkId]) REFERENCES [TaskLink] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    CREATE TABLE [StudentSession] (
        [Id] uniqueidentifier NOT NULL,
        [CollegeId] uniqueidentifier NULL,
        [SessionHash] binary(32) NOT NULL,
        [TaskLinkId] uniqueidentifier NOT NULL,
        [StdMstId] uniqueidentifier NOT NULL,
        [TaskTypeCode] nvarchar(30) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [LastSeenAt] datetime2 NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [EndedAt] datetime2 NULL,
        CONSTRAINT [PK_StudentSession] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StudentSession_College_CollegeId] FOREIGN KEY ([CollegeId]) REFERENCES [College] ([CollegeId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StudentSession_StudentMaster_StdMstId] FOREIGN KEY ([StdMstId]) REFERENCES [StudentMaster] ([StdMstId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StudentSession_TaskLink_TaskLinkId] FOREIGN KEY ([TaskLinkId]) REFERENCES [TaskLink] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Code', N'ExpiryRule', N'Name', N'RequiresOtp') AND [object_id] = OBJECT_ID(N'[TaskType]'))
        SET IDENTITY_INSERT [TaskType] ON;
    EXEC(N'INSERT INTO [TaskType] ([Code], [ExpiryRule], [Name], [RequiresOtp])
    VALUES (N''ATKT_PAYMENT'', N''DIE_ON_PAYMENT'', N''Pay ATKT exam fee'', CAST(1 AS bit)),
    (N''HALL_TICKET'', N''DIE_ON_DEADLINE'', N''Download hall ticket'', CAST(1 AS bit)),
    (N''MARKSHEET'', N''DIE_ON_DEADLINE'', N''Download marksheet'', CAST(1 AS bit)),
    (N''STUDENT_MASTER'', N''DIE_ON_DEADLINE'', N''Verify student details'', CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Code', N'ExpiryRule', N'Name', N'RequiresOtp') AND [object_id] = OBJECT_ID(N'[TaskType]'))
        SET IDENTITY_INSERT [TaskType] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN

    EXEC(N'UPDATE StudentMaster SET Email = NULLIF(LOWER(LTRIM(RTRIM(Email))), '''') WHERE Email IS NOT NULL;
    WITH d AS (
        SELECT StdMstId, Email, ROW_NUMBER() OVER (PARTITION BY CollegeId, Email ORDER BY CreatedAt, StdMstId) AS n
        FROM StudentMaster WHERE Email IS NOT NULL AND IsDeleted = 0)
    UPDATE s SET s.EmailStatus = ''Flagged'', s.EmailFlagReason = LEFT(''Same email as another student: '' + d.Email, 200), s.Email = NULL
    FROM StudentMaster s JOIN d ON d.StdMstId = s.StdMstId WHERE d.n > 1;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_StudentMaster_CollegeId_Email] ON [StudentMaster] ([CollegeId], [Email]) WHERE [Email] IS NOT NULL AND [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_CollegeSettings_CollegeId] ON [CollegeSettings] ([CollegeId]) WHERE [CollegeId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    CREATE INDEX [IX_EmailQueue_CollegeId_SentAt] ON [EmailQueue] ([CollegeId], [SentAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    CREATE INDEX [IX_EmailQueue_Status_Priority_NextAttemptAt] ON [EmailQueue] ([Status], [Priority], [NextAttemptAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    CREATE INDEX [IX_OtpChallenge_CollegeId] ON [OtpChallenge] ([CollegeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    CREATE INDEX [IX_OtpChallenge_TaskLinkId_CreatedAt] ON [OtpChallenge] ([TaskLinkId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    CREATE INDEX [IX_StudentSession_CollegeId] ON [StudentSession] ([CollegeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StudentSession_SessionHash] ON [StudentSession] ([SessionHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    CREATE INDEX [IX_StudentSession_StdMstId] ON [StudentSession] ([StdMstId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    CREATE INDEX [IX_StudentSession_TaskLinkId] ON [StudentSession] ([TaskLinkId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    CREATE INDEX [IX_TaskBatch_CollegeId] ON [TaskBatch] ([CollegeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    CREATE INDEX [IX_TaskBatch_TaskTypeCode] ON [TaskBatch] ([TaskTypeCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    CREATE INDEX [IX_TaskLink_BatchId] ON [TaskLink] ([BatchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    CREATE INDEX [IX_TaskLink_CollegeId] ON [TaskLink] ([CollegeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    CREATE INDEX [IX_TaskLink_StdMstId_TaskTypeCode_Status] ON [TaskLink] ([StdMstId], [TaskTypeCode], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    CREATE INDEX [IX_TaskLink_TaskTypeCode] ON [TaskLink] ([TaskTypeCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_TaskLink_TokenHash] ON [TaskLink] ([TokenHash]) WHERE [TokenHash] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016121759_AddStudentAccess'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261016121759_AddStudentAccess', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017054748_AddEmailAccounts'
)
BEGIN
    DECLARE @var sysname;
    SELECT @var = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CollegeSettings]') AND [c].[name] = N'EmailSenderKey');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [CollegeSettings] DROP CONSTRAINT [' + @var + '];');
    ALTER TABLE [CollegeSettings] DROP COLUMN [EmailSenderKey];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017054748_AddEmailAccounts'
)
BEGIN
    CREATE TABLE [EmailAccount] (
        [Id] uniqueidentifier NOT NULL,
        [CollegeId] uniqueidentifier NULL,
        [Provider] nvarchar(20) NOT NULL,
        [Host] nvarchar(255) NOT NULL,
        [Port] int NOT NULL,
        [UseStartTls] bit NOT NULL,
        [Username] nvarchar(255) NOT NULL,
        [PasswordProtected] nvarchar(1000) NULL,
        [FromAddress] nvarchar(255) NOT NULL,
        [FromName] nvarchar(100) NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_EmailAccount] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmailAccount_College_CollegeId] FOREIGN KEY ([CollegeId]) REFERENCES [College] ([CollegeId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017054748_AddEmailAccounts'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_EmailAccount_CollegeId] ON [EmailAccount] ([CollegeId]) WHERE [CollegeId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017054748_AddEmailAccounts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261017054748_AddEmailAccounts', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019063820_AddTaskLinkTracking'
)
BEGIN
    ALTER TABLE [TaskLink] ADD [OpenedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019063820_AddTaskLinkTracking'
)
BEGIN
    ALTER TABLE [TaskBatch] ADD [EditableFields] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019063820_AddTaskLinkTracking'
)
BEGIN
    ALTER TABLE [TaskBatch] ADD [LinkBaseUrl] nvarchar(300) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019063820_AddTaskLinkTracking'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261019063820_AddTaskLinkTracking', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021143306_AddAbcIdAndCollegeSeal'
)
BEGIN
    ALTER TABLE [StudentMaster] ADD [AbcId] nvarchar(12) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021143306_AddAbcIdAndCollegeSeal'
)
BEGIN
    ALTER TABLE [College] ADD [SealUrl] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021143306_AddAbcIdAndCollegeSeal'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261021143306_AddAbcIdAndCollegeSeal', N'9.0.2');
END;

COMMIT;
GO

