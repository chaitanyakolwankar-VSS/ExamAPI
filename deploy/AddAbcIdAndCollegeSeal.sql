-- =======================================================================================
-- AddAbcIdAndCollegeSeal.sql
-- Standalone, idempotent equivalent of migration 20261021143306_AddAbcIdAndCollegeSeal (generated with
-- dotnet ef migrations script --idempotent), for databases updated by hand. Needs AddTaskLinkTracking.sql first.
--
-- StudentMaster.AbcId: the student's ABC ID (12 digits), printed on the hall ticket.
-- College.SealUrl: the office round seal image, printed on the hall ticket.
--
-- Safe to run more than once: it runs only if the migration-history row is still absent.
-- =======================================================================================
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;
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

