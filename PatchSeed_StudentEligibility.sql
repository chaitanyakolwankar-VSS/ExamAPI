-- =======================================================================================
-- PatchSeed_StudentEligibility.sql
-- Backfills StudentEligibilities for students that were seeded without one
-- (SeedDummyData.sql previously skipped this table, so Student Master search /
--  Get Data returned nothing: both queries inner-join StudentEligibilities on AYID).
-- Derives CourseId from StudentMasters and Semester/Pattern/AY from MarksMasters
-- when available; otherwise falls back to the current academic year.
-- Safe to run multiple times.
-- =======================================================================================

BEGIN TRANSACTION;

INSERT INTO StudentEligibilities (Id, StdMstId, StudentId, CourseId, SemesterId, Pattern, AYID, CreatedAt, IsDeleted)
SELECT
    NEWID(),
    sm.StdMstId,
    sm.StudentId,
    sm.CourseId,
    COALESCE(mm.SemesterId, 'Sem-1'),
    mm.Pattern,
    COALESCE(mm.AcademicYearAYID, ay.AYID),
    GETUTCDATE(),
    0
FROM StudentMasters sm
OUTER APPLY (
    SELECT TOP 1 m.SemesterId, m.Pattern, m.AcademicYearAYID
    FROM MarksMasters m
    WHERE m.StdMstId = sm.StdMstId AND m.IsDeleted = 0
    ORDER BY m.CreatedAt DESC
) mm
OUTER APPLY (
    SELECT TOP 1 a.AYID
    FROM AcademicYears a
    WHERE a.IsCurrent = 1 AND a.IsDeleted = 0
    ORDER BY a.CreatedAt DESC
) ay
WHERE sm.IsDeleted = 0
  AND NOT EXISTS (
      SELECT 1 FROM StudentEligibilities se
      WHERE se.StudentId = sm.StudentId AND se.IsDeleted = 0
  );

COMMIT TRANSACTION;

PRINT 'StudentEligibilities backfill complete.';
SELECT StudentId, CourseId, SemesterId, Pattern, AYID
FROM StudentEligibilities
WHERE IsDeleted = 0
ORDER BY StudentId;
