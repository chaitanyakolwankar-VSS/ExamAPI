-- =======================================================================================
-- SeedOrdinances.sql
-- Seeds the Ordinance Engine with the University of Mumbai ordinance rules
-- (per Docs/Ordinance.md - the newer gazette: O.5042-A, O.5043-A, O.5044-A, O.5045-A, O.229)
-- for the CScheme pattern / "Regular" RuleSet, and the O.5042-A GraceLookup chart.
--
-- Idempotent: soft-deletes all existing rules/conditions/actions and GraceLookup rows,
-- then inserts the canonical set. Safe to re-run.
--
-- NOTE on limits specific to T.E. Sem-VI C Scheme (aggregate 775):
--   * 1% of aggregate = 7.75 -> used as MaxLimit for O.5042-A total grace pool.
--     If this RuleSet is reused for an exam with a different aggregate, update that MaxLimit.
-- =======================================================================================

BEGIN TRANSACTION;
SET NOCOUNT ON;

DECLARE @CollegeId  UNIQUEIDENTIFIER = '103EBF99-FEB0-43BC-A312-56FE85D3BCC6';
DECLARE @RuleSetId  UNIQUEIDENTIFIER = '7DD2625D-1E21-4D85-A52C-6490CCA5613E'; -- CScheme / Regular
DECLARE @Now        DATETIME2        = GETUTCDATE();

-- ---------------------------------------------------------------------------------------
-- 0. Make sure the CScheme RuleSet is matched by ExamType (was NULL, relied on name match)
-- ---------------------------------------------------------------------------------------
UPDATE RuleSet SET ExamType = 'Regular', UpdatedAt = @Now WHERE RuleSetId = @RuleSetId;

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
-- 2. Remove ALL existing rules (test/ad-hoc artifacts: 'Test', 'o5042', 'o5043', 'o229' x2,
--    and the mis-parameterised 'O.5045 Condonation'). Soft delete + disable.
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
SELECT r.Priority, r.Name, r.OrdinanceSymbol, r.StopOnSuccess FROM [Rule] r WHERE r.IsDeleted = 0 ORDER BY r.Priority;
SELECT COUNT(*) AS GraceLookupRows FROM GraceLookup WHERE IsDeleted = 0;
SELECT COUNT(*) AS QuotaStudents FROM MarksMaster WHERE IsDeleted = 0 AND QuotaType IS NOT NULL;

COMMIT TRANSACTION;
PRINT 'Ordinance rules seeded successfully.';
