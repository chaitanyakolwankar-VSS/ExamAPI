-- =======================================================================================
-- FixSubjectCreditsWithoutCollege.sql  -  run once in SSMS on GradeSphereApp (safe to run again).
--
-- Subject Master saved credits with the synchronous save, which did not stamp the college, so
-- those SubjectCreditMaster rows have CollegeId = NULL and the tenant filter hides them: staff
-- could not see the credits they had entered. This gives each such row its subject's college.
-- Run it after deploying the API with the fix (2026-10-12). Nothing else is changed.
-- =======================================================================================
USE GradeSphereApp;
SET NOCOUNT ON;

-- What would change, per college
SELECT c.Name AS College, COUNT(*) AS CreditsWithoutCollege
FROM SubjectCreditMaster cm
JOIN SubjectMaster s ON s.SubjectId = cm.SubjectId
JOIN College c ON c.CollegeId = s.CollegeId
WHERE cm.CollegeId IS NULL
GROUP BY c.Name;

BEGIN TRANSACTION;

UPDATE cm
SET cm.CollegeId = s.CollegeId
FROM SubjectCreditMaster cm
JOIN SubjectMaster s ON s.SubjectId = cm.SubjectId
WHERE cm.CollegeId IS NULL AND s.CollegeId IS NOT NULL;

PRINT CONCAT(@@ROWCOUNT, ' credit row(s) given their college.');

COMMIT;

-- Staff who could not see their credits may have entered them again. Subjects listed here have
-- more than one credit entry for the same academic year: open them on Subject Master and delete
-- the extra copy. (Nothing is deleted by this script.)
SELECT c.Name AS College, s.SubjectCode, s.Name AS Subject, s.SemName, s.Pattern, cm.AYID,
       COUNT(*) AS Copies
FROM SubjectCreditMaster cm
JOIN SubjectMaster s ON s.SubjectId = cm.SubjectId
JOIN College c ON c.CollegeId = s.CollegeId
WHERE cm.IsDeleted = 0
GROUP BY c.Name, s.SubjectCode, s.Name, s.SemName, s.Pattern, cm.AYID
HAVING COUNT(*) > 1
ORDER BY c.Name, s.SubjectCode;
