-- =======================================================================================
-- SeedOrdinances.sql
-- Seeds the Ordinance Engine with the University of Mumbai ordinance rules
-- (per Docs/Ordinance.md - the newer gazette: O.5042-A, O.5043-A, O.5044-A, O.5045-A, O.229)
-- onto the *** NEP *** pattern / "Regular" RuleSet, plus the O.5042-A GraceLookup chart.
--
-- WHY NEP (changed 2026-07-25):
--   All seeded test data (69 MarksMaster rows, 69 StudentEligibility rows, the 11 Sem-6
--   subjects) carries Pattern = 'NEP'. ResultService.ProcessResults resolves the RuleSet by
--   RuleSet.Pattern.PatternName == request.Pattern, so rules parked on the CScheme pattern
--   were never reachable. This script now targets NEP so the engine actually fires.
--
-- Idempotent: soft-deletes ALL existing rules/conditions/actions and GraceLookup rows
-- (this is a test-data reset script), then inserts the canonical set. Safe to re-run.
--
-- NOTE on limits specific to the NEP Sem-VI test exam (aggregate 775):
--   Heads: 5 subjects x (Theory 80 + TermWork 20) = 500, CSL601 25+25, CSL602 25,
--   CSL603 25, CSL604 25+25, CSL605 25+50, CSM601 25+25  ->  775 total.
--   * 1% of aggregate = 7.75 -> used as MaxLimit for the O.5042-A total grace pool.
--     If this RuleSet is reused for an exam with a different aggregate, update that MaxLimit.
-- =======================================================================================

BEGIN TRANSACTION;
SET NOCOUNT ON;

DECLARE @CollegeId    UNIQUEIDENTIFIER = '103EBF99-FEB0-43BC-A312-56FE85D3BCC6';
DECLARE @PatternName  NVARCHAR(100)    = 'NEP';
DECLARE @ExamType     NVARCHAR(50)     = 'Regular';
DECLARE @Now          DATETIME2        = GETUTCDATE();

-- ---------------------------------------------------------------------------------------
-- 0. Resolve the target Pattern + RuleSet by NAME (no hard-coded GUIDs, so the script
--    survives a rebuilt database). Creates the RuleSet if the pattern has none yet.
-- ---------------------------------------------------------------------------------------
DECLARE @PatternId     UNIQUEIDENTIFIER;
DECLARE @RuleSetId     UNIQUEIDENTIFIER;
DECLARE @GradeMasterId UNIQUEIDENTIFIER;

SELECT @PatternId = PatternId FROM PatternMaster
WHERE PatternName = @PatternName AND IsDeleted = 0;

IF @PatternId IS NULL
BEGIN
    RAISERROR('Pattern ''%s'' not found in PatternMaster. Create it before seeding ordinances.', 16, 1, @PatternName);
    ROLLBACK TRANSACTION;
    RETURN;
END

-- The grading scale the engine reads thresholds from (RuleSet.GradeMasterId).
SELECT TOP 1 @GradeMasterId = GradeMasterId FROM GradeMaster WHERE IsDeleted = 0 ORDER BY CreatedAt;

IF @GradeMasterId IS NULL
BEGIN
    RAISERROR('No GradeMaster configured. GetGradePointFromPercentage throws without thresholds.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END

SELECT TOP 1 @RuleSetId = RuleSetId FROM RuleSet
WHERE PatternId = @PatternId AND IsDeleted = 0
ORDER BY CASE WHEN ExamType = @ExamType THEN 0 ELSE 1 END, CreatedAt;

IF @RuleSetId IS NULL
BEGIN
    SET @RuleSetId = NEWID();
    INSERT INTO RuleSet (RuleSetId, Name, ExamType, IsActive, PatternId, GradeMasterId, CreatedAt, IsDeleted)
    VALUES (@RuleSetId, 'Regular', @ExamType, 1, @PatternId, @GradeMasterId, @Now, 0);
END
ELSE
BEGIN
    -- Match by ExamType explicitly rather than relying on the RuleSet.Name fallback
    -- (ResultService.IsRuleSetForExamType) and make sure it is active + graded.
    UPDATE RuleSet
    SET ExamType      = @ExamType,
        IsActive      = 1,
        GradeMasterId = ISNULL(GradeMasterId, @GradeMasterId),
        UpdatedAt     = @Now
    WHERE RuleSetId = @RuleSetId;
END

-- ---------------------------------------------------------------------------------------
-- 1. GraceLookup: O.5042-A chart (Head of Passing -> Grace Marks Upto)
--    Newer gazette version: includes the 301-350 -> 8 and 351-400 -> 9 split
--    (the older O.5042 chart in Docs/ord.md lacked 301-350/8).
-- ---------------------------------------------------------------------------------------
UPDATE GraceLookup SET IsDeleted = 1, DeletedAt = @Now WHERE IsDeleted = 0;

INSERT INTO GraceLookup (GraceLookupId, HeadMarksUpto, GraceMarks, CollegeId, CreatedAt, IsDeleted) VALUES
 (NEWID(),   50,  2, @CollegeId, @Now, 0),
 (NEWID(),  100,  3, @CollegeId, @Now, 0),
 (NEWID(),  150,  4, @CollegeId, @Now, 0),
 (NEWID(),  200,  5, @CollegeId, @Now, 0),
 (NEWID(),  250,  6, @CollegeId, @Now, 0),
 (NEWID(),  300,  7, @CollegeId, @Now, 0),
 (NEWID(),  350,  8, @CollegeId, @Now, 0),
 (NEWID(),  400,  9, @CollegeId, @Now, 0),
 (NEWID(), 9999, 10, @CollegeId, @Now, 0);

-- ---------------------------------------------------------------------------------------
-- 2. Remove ALL existing rules across every RuleSet (test/ad-hoc artifacts, plus the
--    previous CScheme-targeted copy of this same rule set). Soft delete + disable, so the
--    NEP RuleSet is the only one the engine can ever resolve rules from.
-- ---------------------------------------------------------------------------------------
UPDATE RuleAction    SET IsDeleted = 1, DeletedAt = @Now WHERE IsDeleted = 0;
UPDATE RuleCondition SET IsDeleted = 1, DeletedAt = @Now WHERE IsDeleted = 0;
UPDATE [Rule]        SET IsDeleted = 1, IsEnabled = 0, DeletedAt = @Now WHERE IsDeleted = 0;

-- ---------------------------------------------------------------------------------------
-- 3. O.5045-A Condonation (*)  -- Priority 1
--    IF the student fails in exactly ONE head of passing,
--    condone the deficiency up to MIN(1% of aggregate, 10% of that head), max 10 marks.
-- ---------------------------------------------------------------------------------------
DECLARE @R1 UNIQUEIDENTIFIER = NEWID();
INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, OrdinanceSymbol, RuleSetId, CreatedAt, IsDeleted)
VALUES (@R1, 'O.5045-A Condonation', 1, 1, 0, '*', @RuleSetId, @Now, 0);

INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted)
VALUES (NEWID(), 'FailedHeadCount', 'Equals', '1', @R1, @Now, 0);

INSERT INTO RuleAction (ActionId, ActionType, CalculationMode, Param1Type, Param1Value, Param2Type, Param2Value, MaxLimit, MaxTargetCount, Target, RuleId, CreatedAt, IsDeleted)
VALUES (NEWID(), 'AddGrace', 'MinOf', 'PercentOfAggregate', 1.00, 'PercentOfSubject', 10.00, 10.00, 1, 'FailingHeads', @R1, @Now, 0);

-- ---------------------------------------------------------------------------------------
-- 4. O.5042-A Grace Marks for passing a head (@)  -- Priority 2
--    Per-head grace from the chart (by the head's maximum marks), total across all heads
--    capped at 1% of the aggregate (7.75 for this 775-mark exam).
--    Chart is encoded as an NCalc expression on SubjectOutOf (per-head OutOf).
-- ---------------------------------------------------------------------------------------
DECLARE @R2 UNIQUEIDENTIFIER = NEWID();
INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, OrdinanceSymbol, RuleSetId, CreatedAt, IsDeleted)
VALUES (@R2, 'O.5042-A Grace for Head Passing', 2, 1, 0, '@', @RuleSetId, @Now, 0);

INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted)
VALUES (NEWID(), 'FailedHeadCount', 'GreaterOrEqual', '1', @R2, @Now, 0);

INSERT INTO RuleAction (ActionId, ActionType, CalculationMode, Param1Type, Param1Value, Param2Type, Param2Value, MaxLimit, MaxTargetCount, Target, Expression, RuleId, CreatedAt, IsDeleted)
VALUES (NEWID(), 'AddGrace', 'MinOf', 'GraceChart', 0.00, 'None', NULL, 7.75, 0, 'FailingHeads',
 'if(SubjectOutOf<=50,2,if(SubjectOutOf<=100,3,if(SubjectOutOf<=150,4,if(SubjectOutOf<=200,5,if(SubjectOutOf<=250,6,if(SubjectOutOf<=300,7,if(SubjectOutOf<=350,8,if(SubjectOutOf<=400,9,10))))))))',
 @R2, @Now, 0);

-- ---------------------------------------------------------------------------------------
-- 5. O.229 NSS/NCC/Extra-Curricular grace on failed heads (#)  -- Priority 3
--    IF the student has a quota (NSS/NCC/Sports/DLLE...) AND has failed head(s),
--    grace up to 5% of the head's maximum per head, 10 marks total.
-- ---------------------------------------------------------------------------------------
DECLARE @R3 UNIQUEIDENTIFIER = NEWID();
INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, OrdinanceSymbol, RuleSetId, CreatedAt, IsDeleted)
VALUES (@R3, 'O.229 NSS/NCC Grace (Failed Heads)', 3, 1, 0, '#', @RuleSetId, @Now, 0);

INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted) VALUES
 (NEWID(), 'HasQuota', 'Equals', '1', @R3, @Now, 0),
 (NEWID(), 'FailedHeadCount', 'GreaterOrEqual', '1', @R3, @Now, 0);

INSERT INTO RuleAction (ActionId, ActionType, CalculationMode, Param1Type, Param1Value, Param2Type, Param2Value, MaxLimit, MaxTargetCount, Target, RuleId, CreatedAt, IsDeleted)
VALUES (NEWID(), 'AddGrace', 'MinOf', 'PercentOfSubject', 5.00, 'None', NULL, 10.00, 0, 'FailingHeads', @R3, @Now, 0);

-- ---------------------------------------------------------------------------------------
-- 6. O.229 class/aggregate benefit (#)  -- Priority 4
--    A quota student who passes all heads gets +0.10 SGPI (the college's implementation
--    of O.229 clauses 4/5 "class improvement" under the CBCS grading system -- verified
--    against the May 2025 gazette: every 'P#' student's SGPI is exactly raw SGPI + 0.10).
--    AddBonusSGPIHandler also appends the '#' symbol to the result remark.
-- ---------------------------------------------------------------------------------------
DECLARE @R4 UNIQUEIDENTIFIER = NEWID();
INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, OrdinanceSymbol, RuleSetId, CreatedAt, IsDeleted)
VALUES (@R4, 'O.229 NSS/NCC Class Benefit', 4, 1, 0, '#', @RuleSetId, @Now, 0);

INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted) VALUES
 (NEWID(), 'HasQuota', 'Equals', '1', @R4, @Now, 0),
 (NEWID(), 'FailedHeadCount', 'Equals', '0', @R4, @Now, 0);

INSERT INTO RuleAction (ActionId, ActionType, Param1Value, MaxLimit, RuleId, CreatedAt, IsDeleted)
VALUES (NEWID(), 'AddBonusSGPI', 0.10, 10.00, @R4, @Now, 0);

-- ---------------------------------------------------------------------------------------
-- 7. O.5044-A Grace for Distinction / Grade 'O' (@)  -- Priority 5  [DISABLED]
--    Passes all heads without grace; up to 3 marks in max 2 subjects to reach the next
--    grade. (Engine cannot cap 3 per subject individually: pool of 6 across 2 targets.)
--    DISABLED (IsEnabled=0): the engine's UpgradeGrade action upgrades individual subject
--    grades, but O.5044 applies to distinction in a subject as awarded by the university;
--    the May 2025 gazette shows no '@' upgrades, so enabling this would change grades/SGPI
--    away from the published gazette. Enable deliberately after review.
-- ---------------------------------------------------------------------------------------
DECLARE @R5 UNIQUEIDENTIFIER = NEWID();
INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, OrdinanceSymbol, RuleSetId, CreatedAt, IsDeleted)
VALUES (@R5, 'O.5044-A Distinction Grace', 5, 0, 0, '@', @RuleSetId, @Now, 0);

INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted)
VALUES (NEWID(), 'FailedHeadCount', 'Equals', '0', @R5, @Now, 0);

INSERT INTO RuleAction (ActionId, ActionType, Param1Value, Param2Value, MaxTargetCount, Target, RuleId, CreatedAt, IsDeleted)
VALUES (NEWID(), 'UpgradeGrade', 0.00, 6.00, 2, 'All', @R5, @Now, 0);

-- ---------------------------------------------------------------------------------------
-- 8. O.5043-A Grace for Higher Class / Grade (@)  -- Priority 6  [DISABLED]
--    Passes all heads without grace; short of the next grade by not more than
--    MIN(1% of aggregate, 10 marks). UpgradeGradeHandler aborts if any grace was used.
--    DISABLED (IsEnabled=0): O.5043 applies to the AGGREGATE class boundary, but the
--    engine's UpgradeGrade action upgrades per-subject grades -- with a 7.75-mark pool it
--    would upgrade a subject for most students, diverging from the published gazette
--    (which shows no '@'). Enable deliberately after review.
-- ---------------------------------------------------------------------------------------
DECLARE @R6 UNIQUEIDENTIFIER = NEWID();
INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, OrdinanceSymbol, RuleSetId, CreatedAt, IsDeleted)
VALUES (@R6, 'O.5043-A Higher Grade', 6, 0, 0, '@', @RuleSetId, @Now, 0);

INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted)
VALUES (NEWID(), 'FailedHeadCount', 'Equals', '0', @R6, @Now, 0);

INSERT INTO RuleAction (ActionId, ActionType, Param1Value, Param2Value, MaxTargetCount, Target, RuleId, CreatedAt, IsDeleted)
VALUES (NEWID(), 'UpgradeGrade', 1.00, 10.00, 1, 'All', @R6, @Now, 0);

-- ---------------------------------------------------------------------------------------
-- 9. QuotaType for the 9 students shown as 'P#' in the May 2025 gazette (O.229 claimed).
--    Gazette does not say WHICH activity; defaulting to NSS -- correct individually if
--    the college records say NCC/Sports/DLLE etc. Only fills where QuotaType is empty.
-- ---------------------------------------------------------------------------------------
UPDATE MarksMaster SET QuotaType = 'NSS', UpdatedAt = @Now
WHERE IsDeleted = 0 AND (QuotaType IS NULL OR QuotaType = '')
  AND SeatNo IN ('6242012','6242015','6242031','6242040','6242056','6242057','6242063','6242069','6242073');

-- ---------------------------------------------------------------------------------------
-- Verification output
-- ---------------------------------------------------------------------------------------
SELECT 'Target RuleSet' AS Info, p.PatternName, rs.Name, rs.ExamType, rs.IsActive, gm.Name AS GradeScale
FROM RuleSet rs
JOIN PatternMaster p ON p.PatternId = rs.PatternId
LEFT JOIN GradeMaster gm ON gm.GradeMasterId = rs.GradeMasterId
WHERE rs.RuleSetId = @RuleSetId;

SELECT p.PatternName, r.Priority, r.Name, r.OrdinanceSymbol, r.IsEnabled, r.StopOnSuccess
FROM [Rule] r
JOIN RuleSet rs ON rs.RuleSetId = r.RuleSetId
JOIN PatternMaster p ON p.PatternId = rs.PatternId
WHERE r.IsDeleted = 0 ORDER BY p.PatternName, r.Priority;

SELECT COUNT(*) AS GraceLookupRows FROM GraceLookup WHERE IsDeleted = 0;
SELECT COUNT(*) AS QuotaStudents FROM MarksMaster WHERE IsDeleted = 0 AND QuotaType IS NOT NULL;

COMMIT TRANSACTION;
PRINT 'Ordinance rules seeded successfully onto the NEP pattern.';
