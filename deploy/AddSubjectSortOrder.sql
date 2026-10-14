-- =======================================================================================
-- AddSubjectSortOrder.sql
-- Standalone, idempotent equivalent of migration 20261014045830_AddSubjectSortOrder, for live / dev
-- databases that are updated by hand instead of through the idempotent EF script.
--
-- Subject Master gets an order number (SubjectMaster.SortOrder, int NULL) that staff set; every list of
-- subjects follows it (NULLs last, then subject code). Existing subjects are numbered 1..n inside their
-- college / branch / pattern / semester, in the order they were created (then by code), so nothing
-- changes visibly until staff reorder. Soft-deleted subjects stay NULL.
--
-- Safe to run more than once: the column is added only if missing, the backfill only touches rows that
-- still have SortOrder NULL, and the migration-history row is inserted only if absent.
-- =======================================================================================
SET NOCOUNT ON;

BEGIN TRANSACTION;

IF COL_LENGTH('SubjectMaster', 'SortOrder') IS NULL
    ALTER TABLE SubjectMaster ADD SortOrder int NULL;

-- Dynamic SQL so this batch compiles even though the column does not exist yet when the batch starts.
EXEC('
UPDATE s SET s.SortOrder = n.rn
FROM SubjectMaster s
JOIN (
    SELECT SubjectId,
           ROW_NUMBER() OVER (PARTITION BY CollegeId, CourseId, Pattern, SemId
                              ORDER BY CreatedAt, SubjectCode) AS rn
    FROM SubjectMaster
    WHERE IsDeleted = 0
) n ON n.SubjectId = s.SubjectId
WHERE s.SortOrder IS NULL;');

IF OBJECT_ID('__EFMigrationsHistory') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = '20261014045830_AddSubjectSortOrder')
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES ('20261014045830_AddSubjectSortOrder', '9.0.2');

COMMIT;
PRINT 'SubjectMaster.SortOrder is in place and backfilled.';
