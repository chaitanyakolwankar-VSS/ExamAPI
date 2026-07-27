-- =======================================================================================
-- CleanupJunkData.sql
-- Soft-deletes leftover ad-hoc/test artifacts so the database contains only the NEP
-- Sem-VI test dataset. Idempotent and REVERSIBLE (sets IsDeleted = 1; nothing is
-- physically removed, so any row can be restored by setting IsDeleted back to 0).
--
-- Verified safe before writing this script:
--   * SubjectMaster 'sub001' has 0 StudentMarks, 0 SubjectCreditMaster, 0 TimeTableMaster rows.
--   * RuleSet 'test 2 ' (CBGS) has 0 live Rules.
--   * No orphaned SubjectCreditMaster or StudentMarks rows exist.
--
-- Deliberately NOT touched:
--   * PatternMaster rows CScheme / CBGS / NEP -- all are real scheme names.
--   * The empty CScheme RuleSet -- a legitimate (if currently unused) shell; the
--     ordinance rules were moved off it to NEP by SeedOrdinances.sql.
-- =======================================================================================

BEGIN TRANSACTION;
SET NOCOUNT ON;

DECLARE @Now DATETIME2 = GETUTCDATE();

-- ---------------------------------------------------------------------------------------
-- 1. Junk subject 'sub001' / "qwerty" (Sem-3, CScheme) -- placeholder from manual UI testing.
--    Guarded: only deletes while it still has no dependent rows.
-- ---------------------------------------------------------------------------------------
UPDATE sm SET IsDeleted = 1, DeletedAt = @Now
FROM SubjectMaster sm
WHERE sm.IsDeleted = 0
  AND sm.SubjectCode = 'sub001'
  AND NOT EXISTS (SELECT 1 FROM StudentMarks       x WHERE x.SubjectId = sm.SubjectId AND x.IsDeleted = 0)
  AND NOT EXISTS (SELECT 1 FROM SubjectCreditMaster c WHERE c.SubjectId = sm.SubjectId AND c.IsDeleted = 0)
  AND NOT EXISTS (SELECT 1 FROM TimeTableMaster     t WHERE t.SubjectId = sm.SubjectId AND t.IsDeleted = 0);

-- ---------------------------------------------------------------------------------------
-- 2. Junk RuleSet 'test 2 ' on the CBGS pattern -- has no live rules, so it can only ever
--    make RuleSet resolution ambiguous. Guarded on the rule count.
-- ---------------------------------------------------------------------------------------
UPDATE rs SET IsDeleted = 1, DeletedAt = @Now, IsActive = 0
FROM RuleSet rs
WHERE rs.IsDeleted = 0
  AND LTRIM(RTRIM(rs.Name)) = 'test 2'
  AND NOT EXISTS (SELECT 1 FROM [Rule] r WHERE r.RuleSetId = rs.RuleSetId AND r.IsDeleted = 0);

-- ---------------------------------------------------------------------------------------
-- Verification output -- everything below should be NEP-only.
-- ---------------------------------------------------------------------------------------
SELECT 'Live subjects by pattern' AS Info, Pattern, COUNT(*) AS Subjects
FROM SubjectMaster WHERE IsDeleted = 0 GROUP BY Pattern;

SELECT 'Live rule sets' AS Info, p.PatternName, rs.Name, rs.ExamType,
       (SELECT COUNT(*) FROM [Rule] r WHERE r.RuleSetId = rs.RuleSetId AND r.IsDeleted = 0) AS LiveRules
FROM RuleSet rs JOIN PatternMaster p ON p.PatternId = rs.PatternId
WHERE rs.IsDeleted = 0;

COMMIT TRANSACTION;
PRINT 'Junk data cleaned (soft delete).';
