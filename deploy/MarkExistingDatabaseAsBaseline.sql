-- =======================================================================================
-- MarkExistingDatabaseAsBaseline.sql
-- The 26 old migrations were merged into one: 20261005045359_InitialSchema.
-- A NEW database needs nothing from this file (dotnet ef database update / deploy.sql builds it).
-- An EXISTING database built by the old chain (the demo database, a team member's local copy)
-- already has the same tables; this script only rewrites its migration history so EF treats
-- it as being on the new baseline. Run it once, after the database has the last old
-- migration (DropLegacyColumns), otherwise it refuses.
-- =======================================================================================
SET NOCOUNT ON;

IF EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = '20261005045359_InitialSchema')
BEGIN
    PRINT 'Already on the baseline; nothing to do.';
    RETURN;
END

IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = '20261005045131_DropLegacyColumns')
BEGIN
    RAISERROR('This database does not have the last old migration (DropLegacyColumns). Update it with the old code first, or recreate it from the baseline.', 16, 1);
    RETURN;
END

BEGIN TRANSACTION;
DELETE FROM __EFMigrationsHistory;
INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20261005045359_InitialSchema', '9.0.2');
COMMIT;
PRINT 'Marked as 20261005045359_InitialSchema.';
