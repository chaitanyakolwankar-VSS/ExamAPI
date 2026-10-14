-- =======================================================================================
-- FixDoubledSubjectHeads.sql  -  run in SSMS on GradeSphereApp (safe to run again).
--
-- Some subjects had their credits saved twice for the same academic year (two credit entries, each with
-- the same heads). Every student assigned to an exam after that got BOTH copies' heads, so Marks Entry
-- shows 60, 40, 60, 40 instead of 60, 40. (Save Credits and exam assignment now refuse this.)
--
-- For each such subject + academic year this:
--   * keeps ONE credit entry: the one most students are assigned to (then the oldest);
--   * keeps ONE mark row per student per head: the one with marks typed in, else the kept entry's row;
--     that row is moved onto the kept entry if it was on the extra one;
--   * soft-deletes (IsDeleted = 1) the other mark rows and the extra credit entry with its heads.
-- Nothing is hard-deleted.
--
-- A subject is LEFT ALONE (listed for a person to decide) when:
--   * its copies have different heads / out-of / passing, or
--   * results or resolutions were already processed on the extra copy, or
--   * a student has DIFFERENT marks typed into both columns of the same head.
--
-- Step 1: run as is (@Apply = 0): it only SHOWS what it would do.
-- Step 2: set @Apply = 1 and run again.
-- =======================================================================================
USE GradeSphereApp;
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @Apply bit = 0;

IF OBJECT_ID('tempdb..#copies') IS NOT NULL DROP TABLE #copies;
IF OBJECT_ID('tempdb..#rows') IS NOT NULL DROP TABLE #rows;

-- 1. Every live credit entry of a subject + year that has more than one.
SELECT cm.CreditsId, cm.SubjectId, cm.AYID, cm.CreatedAt,
       (SELECT STRING_AGG(CONCAT(h.Head, ':', h.HeadOutOf, '/', h.HeadPass), ',') WITHIN GROUP (ORDER BY h.Head)
        FROM SubjectCredits h WHERE h.CreditsId = cm.CreditsId AND h.IsDeleted = 0) AS Heads,
       (SELECT COUNT(DISTINCT m.MarksId) FROM StudentMarks m WHERE m.CreditsId = cm.CreditsId AND m.IsDeleted = 0) AS Students,
       (SELECT COUNT(*) FROM StudentSubjectResult r WHERE r.CreditsId = cm.CreditsId AND r.IsDeleted = 0)
     + (SELECT COUNT(*) FROM ResolutionMaster rm
        WHERE rm.CreditID = cm.CreditsId
           OR rm.SubjectCreditID IN (SELECT h.Id FROM SubjectCredits h WHERE h.CreditsId = cm.CreditsId)) AS Processed,
       CAST(0 AS bit) AS IsKeep,
       CAST(NULL AS uniqueidentifier) AS KeepId,
       CAST('' AS varchar(80)) AS GroupAction
INTO #copies
FROM SubjectCreditMaster cm
WHERE cm.IsDeleted = 0
  AND (SELECT COUNT(*) FROM SubjectCreditMaster o
       WHERE o.IsDeleted = 0 AND o.SubjectId = cm.SubjectId AND o.AYID = cm.AYID) > 1;

-- 2. The entry to keep: most students assigned, then the oldest.
WITH ranked AS (
    SELECT c.CreditsId, c.SubjectId, c.AYID,
           ROW_NUMBER() OVER (PARTITION BY c.SubjectId, c.AYID ORDER BY c.Students DESC, c.CreatedAt, c.CreditsId) AS Pick
    FROM #copies c
)
UPDATE c SET c.KeepId = k.CreditsId, c.IsKeep = CASE WHEN c.CreditsId = k.CreditsId THEN 1 ELSE 0 END
FROM #copies c
JOIN ranked k ON k.SubjectId = c.SubjectId AND k.AYID = c.AYID AND k.Pick = 1;

-- 3. Every mark row on those entries, with the one to keep per student + head.
SELECT m.Id, m.MarksId, m.Head, m.CreditsId, c.KeepId, c.SubjectId, c.AYID,
       m.RawMarks, m.Marks, m.IsAbsent, m.Grace, m.Resolution,
       CASE WHEN m.RawMarks IS NOT NULL OR m.Marks IS NOT NULL OR m.IsAbsent = 1
                 OR m.Resolution IS NOT NULL OR ISNULL(m.Grace, '') <> '' THEN 1 ELSE 0 END AS HasData,
       ROW_NUMBER() OVER (PARTITION BY c.SubjectId, c.AYID, m.MarksId, m.Head
                          ORDER BY CASE WHEN m.RawMarks IS NOT NULL OR m.Marks IS NOT NULL OR m.IsAbsent = 1
                                             OR m.Resolution IS NOT NULL OR ISNULL(m.Grace, '') <> '' THEN 0 ELSE 1 END,
                                   CASE WHEN m.CreditsId = c.KeepId THEN 0 ELSE 1 END,
                                   m.CreatedAt, m.Id) AS Pick
INTO #rows
FROM StudentMarks m
JOIN #copies c ON c.CreditsId = m.CreditsId
WHERE m.IsDeleted = 0;

-- 4. Decide per subject + year.
UPDATE c SET c.GroupAction =
    CASE
        WHEN (SELECT COUNT(DISTINCT ISNULL(o.Heads, '')) FROM #copies o WHERE o.SubjectId = c.SubjectId AND o.AYID = c.AYID) > 1
            THEN 'LEAVE: copies have different heads'
        WHEN EXISTS (SELECT 1 FROM #copies o WHERE o.SubjectId = c.SubjectId AND o.AYID = c.AYID AND o.IsKeep = 0 AND o.Processed > 0)
            THEN 'LEAVE: results already processed on the extra copy'
        WHEN EXISTS (SELECT 1 FROM #rows r
                     WHERE r.SubjectId = c.SubjectId AND r.AYID = c.AYID AND r.HasData = 1
                     GROUP BY r.MarksId, r.Head
                     HAVING COUNT(DISTINCT CONCAT(r.RawMarks, '|', r.Marks, '|', r.IsAbsent, '|', r.Grace, '|', r.Resolution)) > 1)
            THEN 'LEAVE: a student has different marks in both columns'
        ELSE 'FIX'
    END
FROM #copies c;

-- What it found (one line per credit entry)
SELECT col.Name AS College, br.Name AS Branch, s.SubjectCode, s.Name AS Subject, s.SemName, s.Pattern, c.AYID,
       c.GroupAction, CASE WHEN c.IsKeep = 1 THEN 'KEEP' ELSE 'EXTRA' END AS Entry,
       c.Heads, c.Students, c.Processed, c.CreatedAt, c.CreditsId
FROM #copies c
JOIN SubjectMaster s ON s.SubjectId = c.SubjectId
LEFT JOIN CourseMaster br ON br.CourseId = s.CourseId
LEFT JOIN College col ON col.CollegeId = s.CollegeId
ORDER BY col.Name, br.Name, s.SubjectCode, c.AYID, c.IsKeep DESC;

-- What would change in marks (FIX subjects only)
SELECT br.Name AS Branch, s.SubjectCode, s.Name AS Subject,
       COUNT(DISTINCT r.MarksId) AS StudentsAffected,
       SUM(CASE WHEN r.Pick > 1 THEN 1 ELSE 0 END) AS MarkRowsToDelete,
       SUM(CASE WHEN r.Pick = 1 AND r.CreditsId <> r.KeepId THEN 1 ELSE 0 END) AS MarkRowsToMove,
       SUM(CASE WHEN r.Pick > 1 AND r.HasData = 1 THEN 1 ELSE 0 END) AS DeletedRowsWithSameMarks
FROM #rows r
JOIN SubjectMaster s ON s.SubjectId = r.SubjectId
LEFT JOIN CourseMaster br ON br.CourseId = s.CourseId
WHERE EXISTS (SELECT 1 FROM #copies c WHERE c.SubjectId = r.SubjectId AND c.AYID = r.AYID AND c.GroupAction = 'FIX')
GROUP BY br.Name, s.SubjectCode, s.Name
ORDER BY br.Name, s.SubjectCode;

IF @Apply = 1
BEGIN
    BEGIN TRANSACTION;

    DELETE r FROM #rows r
    WHERE NOT EXISTS (SELECT 1 FROM #copies c WHERE c.SubjectId = r.SubjectId AND c.AYID = r.AYID AND c.GroupAction = 'FIX');

    UPDATE m SET m.IsDeleted = 1, m.DeletedAt = SYSUTCDATETIME()
    FROM StudentMarks m JOIN #rows r ON r.Id = m.Id
    WHERE r.Pick > 1;
    PRINT CONCAT(@@ROWCOUNT, ' doubled mark row(s) deleted.');

    UPDATE m SET m.CreditsId = r.KeepId, m.UpdatedAt = SYSUTCDATETIME()
    FROM StudentMarks m JOIN #rows r ON r.Id = m.Id
    WHERE r.Pick = 1 AND r.CreditsId <> r.KeepId;
    PRINT CONCAT(@@ROWCOUNT, ' mark row(s) moved onto the kept credit entry.');

    UPDATE h SET h.IsDeleted = 1, h.DeletedAt = SYSUTCDATETIME()
    FROM SubjectCredits h JOIN #copies c ON c.CreditsId = h.CreditsId
    WHERE c.GroupAction = 'FIX' AND c.IsKeep = 0 AND h.IsDeleted = 0;

    UPDATE cm SET cm.IsDeleted = 1, cm.DeletedAt = SYSUTCDATETIME()
    FROM SubjectCreditMaster cm JOIN #copies c ON c.CreditsId = cm.CreditsId
    WHERE c.GroupAction = 'FIX' AND c.IsKeep = 0;
    PRINT CONCAT(@@ROWCOUNT, ' extra credit entr(y/ies) deleted.');

    COMMIT;
END
ELSE
    PRINT 'Preview only. Set @Apply = 1 and run again to apply the FIX rows.';

DROP TABLE #rows;
DROP TABLE #copies;
