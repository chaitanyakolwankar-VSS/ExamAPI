-- =======================================================================================
-- AddTaskLinkTracking.sql
-- Standalone, idempotent equivalent of migration 20261019063820_AddTaskLinkTracking (generated with
-- dotnet ef migrations script --idempotent), for databases updated by hand. Needs AddEmailAccounts.sql first.
--
-- TaskBatch.EditableFields: which Student Master fields the students of a batch may change.
-- TaskBatch.LinkBaseUrl: the student site address the batch's links are written with.
-- TaskLink.OpenedAt: when the student first signed in with the link.
--
-- Safe to run more than once: it runs only if the migration-history row is still absent.
-- =======================================================================================
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;
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

COMMIT;
GO

