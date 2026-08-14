-- =======================================================================================
-- BackfillCollegeScope.sql   (Phase 2a)
-- Populates the CollegeId column added to the 12 directly-queried tables.
--
-- Every existing row predates multi-tenancy and belongs to the one college that existed
-- at the time -- now renamed "Engineering College". Rather than assume that, each table is
-- backfilled by FOLLOWING ITS OWN FK PATH to a college, so the value is derived from the
-- data rather than hardcoded. Rows that cannot be traced are reported at the end.
--
-- Idempotent: only fills rows where CollegeId IS NULL.
--
-- Deliberately left NULL:
--   GradeMaster / RuleSet / RoleMaster rows that are meant to be platform-wide templates.
--   A NULL CollegeId on those three means "shared by every college" (ICollegeTemplatable).
--   Here they ARE college-owned, so they are backfilled; the nullability exists for the
--   template concept to land later without another migration.
-- =======================================================================================

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;   -- required: filtered unique indexes exist on these tables

BEGIN TRANSACTION;

-- 1. Derived straight from CourseMaster (which already has CollegeId).
UPDATE e SET e.CollegeId = c.CollegeId
FROM ExamMaster e JOIN CourseMaster c ON c.CourseId = e.CourseId
WHERE e.CollegeId IS NULL;

UPDATE s SET s.CollegeId = c.CollegeId
FROM SubjectMaster s JOIN CourseMaster c ON c.CourseId = s.CourseId
WHERE s.CollegeId IS NULL;

UPDATE se SET se.CollegeId = c.CollegeId
FROM StudentEligibility se JOIN CourseMaster c ON c.CourseId = se.CourseId
WHERE se.CollegeId IS NULL;

UPDATE t SET t.CollegeId = c.CollegeId
FROM TimeTableMaster t JOIN CourseMaster c ON c.CourseId = t.CourseId
WHERE t.CollegeId IS NULL;

UPDATE r SET r.CollegeId = c.CollegeId
FROM ResolutionMaster r JOIN CourseMaster c ON c.CourseId = r.CourseID
WHERE r.CollegeId IS NULL;

-- 2. Derived via StudentMaster (which already has CollegeId).
UPDATE mm SET mm.CollegeId = sm.CollegeId
FROM MarksMaster mm JOIN StudentMaster sm ON sm.StdMstId = mm.StdMstId
WHERE mm.CollegeId IS NULL;

UPDATE sor SET sor.CollegeId = sm.CollegeId
FROM StudentsOverallResult sor JOIN StudentMaster sm ON sm.StdMstId = sor.StdMstId
WHERE sor.CollegeId IS NULL;

-- 3. Derived via SubjectMaster -> CourseMaster.
UPDATE scm SET scm.CollegeId = s.CollegeId
FROM SubjectCreditMaster scm JOIN SubjectMaster s ON s.SubjectId = scm.SubjectId
WHERE scm.CollegeId IS NULL;

-- 4. RuleSet follows PatternMaster (which already has CollegeId).
UPDATE rs SET rs.CollegeId = p.CollegeId
FROM RuleSet rs JOIN PatternMaster p ON p.PatternId = rs.PatternId
WHERE rs.CollegeId IS NULL;

-- 5. GradeMaster and RoleMaster have no FK path to a college at all -- that was the gap
--    this phase closes. Assign them to the sole pre-existing college.
DECLARE @SoleCollegeId UNIQUEIDENTIFIER = (
    SELECT TOP 1 CollegeId FROM College WHERE IsDeleted = 0 ORDER BY CreatedAt);

UPDATE GradeMaster SET CollegeId = @SoleCollegeId WHERE CollegeId IS NULL AND IsDeleted = 0;
UPDATE RoleMaster  SET CollegeId = @SoleCollegeId WHERE CollegeId IS NULL AND IsDeleted = 0;

-- 6. AuditLog: attribute historic entries to the same college.
UPDATE AuditLog SET CollegeId = @SoleCollegeId WHERE CollegeId IS NULL;

-- ---------------------------------------------------------------------------------------
-- Verification: every live row in every scoped table must now have a CollegeId.
-- Any non-zero Remaining is a row the global query filter would make INVISIBLE.
-- ---------------------------------------------------------------------------------------
SELECT 'ExamMaster' AS TableName, COUNT(*) AS Remaining FROM ExamMaster WHERE CollegeId IS NULL AND IsDeleted = 0
UNION ALL SELECT 'SubjectMaster', COUNT(*) FROM SubjectMaster WHERE CollegeId IS NULL AND IsDeleted = 0
UNION ALL SELECT 'MarksMaster', COUNT(*) FROM MarksMaster WHERE CollegeId IS NULL AND IsDeleted = 0
UNION ALL SELECT 'StudentEligibility', COUNT(*) FROM StudentEligibility WHERE CollegeId IS NULL AND IsDeleted = 0
UNION ALL SELECT 'StudentsOverallResult', COUNT(*) FROM StudentsOverallResult WHERE CollegeId IS NULL AND IsDeleted = 0
UNION ALL SELECT 'SubjectCreditMaster', COUNT(*) FROM SubjectCreditMaster WHERE CollegeId IS NULL AND IsDeleted = 0
UNION ALL SELECT 'ResolutionMaster', COUNT(*) FROM ResolutionMaster WHERE CollegeId IS NULL AND IsDeleted = 0
UNION ALL SELECT 'TimeTableMaster', COUNT(*) FROM TimeTableMaster WHERE CollegeId IS NULL AND IsDeleted = 0
UNION ALL SELECT 'RuleSet', COUNT(*) FROM RuleSet WHERE CollegeId IS NULL AND IsDeleted = 0
UNION ALL SELECT 'GradeMaster', COUNT(*) FROM GradeMaster WHERE CollegeId IS NULL AND IsDeleted = 0
UNION ALL SELECT 'RoleMaster', COUNT(*) FROM RoleMaster WHERE CollegeId IS NULL AND IsDeleted = 0
UNION ALL SELECT 'AuditLog', COUNT(*) FROM AuditLog WHERE CollegeId IS NULL;

COMMIT TRANSACTION;
PRINT 'College scope backfilled.';
