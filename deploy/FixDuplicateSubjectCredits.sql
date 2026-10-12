-- =======================================================================================
-- FixDuplicateSubjectCredits.sql  -  run in SSMS on GradeSphereApp (safe to run again).
--
-- While saved credits were hidden (see FixSubjectCreditsWithoutCollege.sql), staff entered some
-- subjects' credits more than once. This keeps ONE credit entry per subject + academic year and
-- soft-deletes the extra copies (IsDeleted = 1, with their heads), so they can still be restored.
--
-- Which copy is kept:
--   * the copy that marks / results / resolutions already use, if exactly one is used;
--   * otherwise (none used) the most recently saved copy.
--   * Subjects where SEVERAL copies are already used are NOT touched -- they are listed for a person.
--
-- Step 1: run as is (@Apply = 0): it only SHOWS every copy and which one would be kept.
--         Check that the kept copy has the right credits / heads.
-- Step 2: set @Apply = 1 and run again to delete the extra copies.
-- =======================================================================================
USE GradeSphereApp;
SET NOCOUNT ON;

DECLARE @Apply bit = 0;

IF OBJECT_ID('tempdb..#copies') IS NOT NULL DROP TABLE #copies;

WITH live AS (
    SELECT cm.CreditsId, cm.SubjectId, cm.AYID, cm.TotalCredits, cm.CreatedAt,
           (SELECT COUNT(*) FROM StudentMarks m WHERE m.CreditsId = cm.CreditsId)
         + (SELECT COUNT(*) FROM StudentSubjectResult r WHERE r.CreditsId = cm.CreditsId)
         + (SELECT COUNT(*) FROM ResolutionMaster rm
            WHERE rm.CreditID = cm.CreditsId
               OR rm.SubjectCreditID IN (SELECT h.Id FROM SubjectCredits h WHERE h.CreditsId = cm.CreditsId)) AS UsedBy
    FROM SubjectCreditMaster cm
    WHERE cm.IsDeleted = 0
),
dup AS (
    SELECT l.*,
           COUNT(*) OVER (PARTITION BY l.SubjectId, l.AYID) AS Copies,
           SUM(CASE WHEN l.UsedBy > 0 THEN 1 ELSE 0 END) OVER (PARTITION BY l.SubjectId, l.AYID) AS UsedCopies,
           ROW_NUMBER() OVER (PARTITION BY l.SubjectId, l.AYID
                              ORDER BY CASE WHEN l.UsedBy > 0 THEN 0 ELSE 1 END, l.CreatedAt DESC, l.CreditsId) AS Pick
    FROM live l
)
SELECT d.*,
       CASE WHEN d.UsedCopies > 1 THEN 'LEAVE (several in use: decide by hand)'
            WHEN d.Pick = 1 THEN 'KEEP'
            ELSE 'DELETE' END AS Action
INTO #copies
FROM dup d
WHERE d.Copies > 1;

-- Every duplicated subject, copy by copy, with its heads (Head out-of/pass)
SELECT c.Name AS College, s.SubjectCode, s.Name AS Subject, s.SemName, s.Pattern, x.AYID,
       x.Action, x.TotalCredits, x.UsedBy, x.CreatedAt,
       (SELECT STRING_AGG(CONCAT(h.Head, ' ', h.HeadOutOf, '/', h.HeadPass), ', ')
        FROM SubjectCredits h WHERE h.CreditsId = x.CreditsId AND h.IsDeleted = 0) AS Heads,
       x.CreditsId
FROM #copies x
JOIN SubjectMaster s ON s.SubjectId = x.SubjectId
LEFT JOIN College c ON c.CollegeId = s.CollegeId
ORDER BY c.Name, s.SubjectCode, x.AYID, x.Pick;

IF @Apply = 1
BEGIN
    BEGIN TRANSACTION;

    UPDATE h SET h.IsDeleted = 1, h.DeletedAt = SYSUTCDATETIME()
    FROM SubjectCredits h
    JOIN #copies x ON x.CreditsId = h.CreditsId
    WHERE x.Action = 'DELETE' AND h.IsDeleted = 0;

    UPDATE cm SET cm.IsDeleted = 1, cm.DeletedAt = SYSUTCDATETIME()
    FROM SubjectCreditMaster cm
    JOIN #copies x ON x.CreditsId = cm.CreditsId
    WHERE x.Action = 'DELETE';

    PRINT CONCAT(@@ROWCOUNT, ' extra credit entr(y/ies) deleted.');
    COMMIT;
END
ELSE
    PRINT 'Preview only. Set @Apply = 1 and run again to delete the rows marked DELETE.';

DROP TABLE #copies;
