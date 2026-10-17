-- =======================================================================================
-- AddEmailAccounts.sql
-- Standalone, idempotent equivalent of migration 20261017054748_AddEmailAccounts (generated with
-- dotnet ef migrations script --idempotent), for databases updated by hand. Needs AddStudentAccess.sql first.
--
-- EmailAccount: the mail accounts student emails are sent from, set on the platform admin page -- one per
-- college plus the platform default (CollegeId NULL). Passwords are stored encrypted by the API.
-- CollegeSettings.EmailSenderKey (never used) is dropped.
--
-- Safe to run more than once: it runs only if the migration-history row is still absent.
-- =======================================================================================
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;
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

COMMIT;
GO

