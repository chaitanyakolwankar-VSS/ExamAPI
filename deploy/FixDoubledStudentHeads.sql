-- =======================================================================================
-- FixDoubledStudentHeads.sql  -  run in SSMS on GradeSphereApp (safe to run again).
--
-- Pressing Save twice on Regular Exam assignment gave students every head of every subject twice, so
-- Marks Entry showed 60, 40, 60, 40 (live, 2026-10-14: Electrical Sem-3, December 2026, 72 students).
-- (Assignment now skips heads a student already has.)
--
-- This keeps ONE mark row per student + subject + credit entry + head -- the one with marks typed in,
-- else the first one created -- and soft-deletes (IsDeleted = 1) the repeats. Nothing is hard-deleted.
-- A student whose two copies hold DIFFERENT marks is left alone and listed for a person.
--
-- Step 1: run as is (@Apply = 0): it only SHOWS what it would do.
-- Step 2: set @Apply = 1 and run again.
-- =======================================================================================
USE GradeSphereApp;
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @Apply bit = 0;

IF OBJECT_ID('tempdb..#rows') IS NOT NULL DROP TABLE #rows;

SELECT m.Id, m.MarksId, m.SubjectId, m.CreditsId, m.Head,
       CASE WHEN m.RawMarks IS NOT NULL OR m.Marks IS NOT NULL OR m.IsAbsent = 1
                 OR m.Resolution IS NOT NULL OR ISNULL(m.Grace, '') <> '' THEN 1 ELSE 0 END AS HasData,
       CONCAT(m.RawMarks, '|', m.Marks, '|', m.IsAbsent, '|', m.Grace, '|', m.Resolution) AS Data,
       ROW_NUMBER() OVER (PARTITION BY m.MarksId, m.SubjectId, m.CreditsId, m.Head
                          ORDER BY CASE WHEN m.RawMarks IS NOT NULL OR m.Marks IS NOT NULL OR m.IsAbsent = 1
                                             OR m.Resolution IS NOT NULL OR ISNULL(m.Grace, '') <> '' THEN 0 ELSE 1 END,
                                   m.CreatedAt, m.Id) AS Pick,
       COUNT(*) OVER (PARTITION BY m.MarksId, m.SubjectId, m.CreditsId, m.Head) AS Copies
INTO #rows
FROM StudentMarks m
WHERE m.IsDeleted = 0;

DELETE FROM #rows WHERE Copies = 1;

-- Students whose copies of a head hold different marks: left alone.
SELECT DISTINCT r.MarksId, r.SubjectId, r.Head
INTO #conflicts
FROM #rows r
WHERE r.HasData = 1
GROUP BY r.MarksId, r.SubjectId, r.CreditsId, r.Head
HAVING COUNT(DISTINCT r.Data) > 1;

SELECT br.Name AS Branch, e.Name AS Exam, mm.SemesterId, s.SubjectCode, s.Name AS Subject,
       COUNT(DISTINCT r.MarksId) AS Students,
       SUM(CASE WHEN r.Pick > 1 THEN 1 ELSE 0 END) AS RowsToDelete,
       SUM(CASE WHEN r.Pick > 1 AND r.HasData = 1 THEN 1 ELSE 0 END) AS DeletedRowsHoldingSameMarks,
       (SELECT COUNT(*) FROM #conflicts c WHERE c.SubjectId = s.SubjectId) AS LeftAloneConflicts
FROM #rows r
JOIN MarksMaster mm ON mm.MarksId = r.MarksId
LEFT JOIN ExamMaster e ON e.ExamId = mm.ExamId
JOIN SubjectMaster s ON s.SubjectId = r.SubjectId
LEFT JOIN CourseMaster br ON br.CourseId = s.CourseId
GROUP BY br.Name, e.Name, mm.SemesterId, s.SubjectId, s.SubjectCode, s.Name
ORDER BY br.Name, e.Name, s.SubjectCode;

IF @Apply = 1
BEGIN
    BEGIN TRANSACTION;

    UPDATE m SET m.IsDeleted = 1, m.DeletedAt = SYSUTCDATETIME()
    FROM StudentMarks m
    JOIN #rows r ON r.Id = m.Id
    WHERE r.Pick > 1
      AND NOT EXISTS (SELECT 1 FROM #conflicts c WHERE c.MarksId = r.MarksId AND c.SubjectId = r.SubjectId AND c.Head = r.Head);
    PRINT CONCAT(@@ROWCOUNT, ' repeated mark row(s) deleted.');

    COMMIT;
END
ELSE
    PRINT 'Preview only. Set @Apply = 1 and run again to delete the repeats.';

DROP TABLE #conflicts;
DROP TABLE #rows;
