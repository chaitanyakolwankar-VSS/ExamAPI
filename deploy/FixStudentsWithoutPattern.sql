-- =======================================================================================
-- FixStudentsWithoutPattern.sql  -  run once in SSMS on GradeSphereApp (safe to run again).
--
-- Students added one by one on Student Master before 2026-10-12 were saved without a pattern,
-- so Regular Exam (which lists students by branch + semester + pattern) never showed them.
-- This gives them their college's pattern -- ONLY for colleges that have exactly one pattern
-- (e.g. VIVA: NEP). Colleges with several patterns are listed and left for a person to decide.
-- Nothing else is changed.
-- =======================================================================================
USE GradeSphereApp;
SET NOCOUNT ON;

-- What would change, per college
SELECT c.Name AS College,
       COUNT(*) AS StudentsWithoutPattern,
       (SELECT COUNT(*) FROM PatternMaster p WHERE p.CollegeId = c.CollegeId AND p.IsDeleted = 0) AS PatternsInCollege,
       CASE WHEN (SELECT COUNT(*) FROM PatternMaster p WHERE p.CollegeId = c.CollegeId AND p.IsDeleted = 0) = 1
            THEN (SELECT MIN(p.PatternName) FROM PatternMaster p WHERE p.CollegeId = c.CollegeId AND p.IsDeleted = 0)
            ELSE '(several: left as is)' END AS PatternToUse
FROM StudentEligibility e
JOIN College c ON c.CollegeId = e.CollegeId
WHERE e.IsDeleted = 0 AND (e.Pattern IS NULL OR LTRIM(RTRIM(e.Pattern)) = '')
GROUP BY c.CollegeId, c.Name;

BEGIN TRANSACTION;

UPDATE e
SET e.Pattern = p.PatternName
FROM StudentEligibility e
JOIN PatternMaster p ON p.CollegeId = e.CollegeId AND p.IsDeleted = 0
WHERE e.IsDeleted = 0
  AND (e.Pattern IS NULL OR LTRIM(RTRIM(e.Pattern)) = '')
  AND (SELECT COUNT(*) FROM PatternMaster p2 WHERE p2.CollegeId = e.CollegeId AND p2.IsDeleted = 0) = 1;

PRINT CONCAT(@@ROWCOUNT, ' student enrolment(s) given their college''s pattern.');

COMMIT;

-- Anything still without a pattern (colleges with more than one pattern): fix by hand or re-add.
SELECT c.Name AS College, e.StudentId, e.SemesterId
FROM StudentEligibility e
JOIN College c ON c.CollegeId = e.CollegeId
WHERE e.IsDeleted = 0 AND (e.Pattern IS NULL OR LTRIM(RTRIM(e.Pattern)) = '');
