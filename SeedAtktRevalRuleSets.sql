-- =======================================================================================
-- SeedAtktRevalRuleSets.sql
-- Configures the ATKT / Revaluation exam-assignment screen (the migration of legacy
-- frm_atktreval_exm_assign.aspx) using the EXISTING Ordinance rule engine. No new table.
--
-- HOW THE SCREEN IS CONFIGURED
--   AtktRevalExamService resolves a RuleSet by (pattern, target exam's ExamType), the same
--   way ResultService does. Inside it, any rule carrying an 'AllowExamAssignment' action
--   grants assignment:
--
--     RuleCondition          -> the eligibility gate, evaluated per student against their
--                               source attempt using the normal IFactProvider registry
--                               (FailedSubjectCount, SemesterNo, HasQuota, ...).
--     RuleAction.Target      -> which subjects and which heads are in scope.
--     RuleAction.MaxTargetCount -> cap on subjects per student (0 = no cap).
--
--   These replace the literals the legacy code-behind hard-coded:
--     exam_code LIKE 'EXM%' AND atkt_exam = 1  -> RuleSet.ExamType
--     remark = 'UnSuccessful'                  -> Target 'FailingSubjects'
--     Result = 'Pass' -> no checkbox           -> absence of 'PassingSubjects' in Target
--     h1/h2 contains 'Ab' -> no checkbox       -> absence of 'AbsentSubjects' in Target
--     head type literal "ESE"                  -> an 'ESE' head token in Target
--
-- TARGET VOCABULARY (parsed by HeadTargetSpec)
--   Subject scope : AllSubjects | FailingSubjects | PassingSubjects | AbsentSubjects |
--                   NotAttempted        (omit all of them to mean every subject)
--   Head names    : anything else, e.g. ESE. Matched against SubjectCredits.HeadType (the
--                   printed label) AND the positional key StudentMarks.Head ('H1'/'H2'), so
--                   either spelling works. Omit to re-attempt every head of a subject.
--
--   Naming heads matters: only heads inside the scope are blanked for a fresh attempt.
--   Every other head of a selected subject carries its marks forward. That is what makes
--   "re-sit the theory paper, keep the term work" expressible, and it is required for
--   combined-passing subjects, whose verdict is the SUM across heads -- a blanked
--   carry-forward head would drop the total and fail the student on data loss.
--
-- WITHOUT THIS SCRIPT the screen still works on built-in defaults: ATKT offers failed,
-- absent and never-attempted subjects; revaluation offers subjects that have a mark; every
-- head of a selected subject is re-attempted; no cap.
--
-- Idempotent. Safe to re-run.
-- =======================================================================================

BEGIN TRANSACTION;
SET NOCOUNT ON;

DECLARE @CollegeId   UNIQUEIDENTIFIER = '103EBF99-FEB0-43BC-A312-56FE85D3BCC6';
DECLARE @PatternName NVARCHAR(100)    = 'NEP';
DECLARE @Now         DATETIME2        = GETUTCDATE();

DECLARE @PatternId     UNIQUEIDENTIFIER;
DECLARE @GradeMasterId UNIQUEIDENTIFIER;

SELECT @PatternId = PatternId FROM PatternMaster
WHERE PatternName = @PatternName AND IsDeleted = 0;

IF @PatternId IS NULL
BEGIN
    RAISERROR('Pattern "%s" not found. Seed the pattern first.', 16, 1, @PatternName);
    ROLLBACK TRANSACTION;
    RETURN;
END

-- Reuse whatever grade scale the pattern's existing rule sets already use.
SELECT TOP 1 @GradeMasterId = GradeMasterId FROM RuleSet
WHERE PatternId = @PatternId AND GradeMasterId IS NOT NULL AND IsDeleted = 0;

-- ---------------------------------------------------------------------------------------
-- Helper pattern used twice below: resolve-or-create the RuleSet, then replace its
-- assignment rule outright so re-running cannot accumulate duplicates.
-- ---------------------------------------------------------------------------------------

-- =======================================================================================
-- 1. ATKT  (RuleSet.ExamType = 'KT', matching the Ordinance UI's "KT / ATKT" option)
--
--    Eligibility : at least one subject not cleared in the source attempt.
--    Scope       : failing and absent subjects. Passed subjects are absent from the Target,
--                  so they render locked with "Already cleared" -- the legacy rule that a
--                  cleared subject cannot be re-registered.
--    Heads       : none named, so the whole subject is re-sat. To reproduce legacy exactly
--                  (re-sit theory only, keep term work) append your ESE head, e.g.
--                  Target = 'FailingSubjects,AbsentSubjects,ESE'.
-- =======================================================================================
DECLARE @AtktRuleSetId UNIQUEIDENTIFIER;
DECLARE @AtktRuleId    UNIQUEIDENTIFIER;

SELECT @AtktRuleSetId = RuleSetId FROM RuleSet
WHERE PatternId = @PatternId AND ExamType = 'KT' AND IsDeleted = 0;

IF @AtktRuleSetId IS NULL
BEGIN
    SET @AtktRuleSetId = NEWID();
    INSERT INTO RuleSet (RuleSetId, CollegeId, Name, ExamType, IsActive, PatternId, GradeMasterId, CreatedAt, IsDeleted)
    VALUES (@AtktRuleSetId, @CollegeId, 'ATKT', 'KT', 1, @PatternId, @GradeMasterId, @Now, 0);
END
ELSE
BEGIN
    UPDATE RuleSet SET IsActive = 1, UpdatedAt = @Now WHERE RuleSetId = @AtktRuleSetId;
END

-- Replace the assignment rule (and only that rule -- ordinance rules in this set are left
-- alone, they govern how the ATKT exam's results are computed).
SELECT @AtktRuleId = r.RuleId
FROM [Rule] r
WHERE r.RuleSetId = @AtktRuleSetId AND r.Name = 'Assign backlog subjects' AND r.IsDeleted = 0;

IF @AtktRuleId IS NOT NULL
BEGIN
    DELETE FROM RuleCondition WHERE RuleId = @AtktRuleId;
    DELETE FROM RuleAction    WHERE RuleId = @AtktRuleId;
END
ELSE
BEGIN
    SET @AtktRuleId = NEWID();
    INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, RuleSetId, CreatedAt, IsDeleted)
    VALUES (@AtktRuleId, 'Assign backlog subjects', 10, 1, 0, @AtktRuleSetId, @Now, 0);
END

INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted)
VALUES (NEWID(), 'FailedSubjectCount', '>', '0', @AtktRuleId, @Now, 0);

INSERT INTO RuleAction (ActionId, ActionType, CalculationMode, Param1Type, Param1Value,
                        Param2Type, Param2Value, MaxLimit, MaxTargetCount, Target,
                        Expression, RuleId, CreatedAt, IsDeleted)
VALUES (NEWID(), 'AllowExamAssignment', 'Fixed', 'None', NULL,
        'None', NULL, NULL, 0, 'FailingSubjects,AbsentSubjects',
        NULL, @AtktRuleId, @Now, 0);

-- =======================================================================================
-- 2. Revaluation  (RuleSet.ExamType = 'REVAL')
--
--    A revaluation exam inherits its parent's ExamType, so it cannot be identified by that.
--    The service detects revaluation from ExamMaster.RevaluationForExamId and then looks for
--    a rule set whose ExamType is REVAL (or REVALUATION).
--
--    Eligibility : none -- anyone who sat the source exam may apply.
--    Scope       : subjects that carry a mark, pass or fail. AbsentSubjects is deliberately
--                  omitted: there is no answer book to revalue. (Legacy comment: "checking
--                  only H1 Ab as college told they dont make absent student in h2".)
--    Heads       : ESE only. Internal and term-work heads are not revaluable, and the marks
--                  for those heads carry forward untouched.
-- =======================================================================================
DECLARE @RevalRuleSetId UNIQUEIDENTIFIER;
DECLARE @RevalRuleId    UNIQUEIDENTIFIER;

SELECT @RevalRuleSetId = RuleSetId FROM RuleSet
WHERE PatternId = @PatternId AND ExamType = 'REVAL' AND IsDeleted = 0;

IF @RevalRuleSetId IS NULL
BEGIN
    SET @RevalRuleSetId = NEWID();
    INSERT INTO RuleSet (RuleSetId, CollegeId, Name, ExamType, IsActive, PatternId, GradeMasterId, CreatedAt, IsDeleted)
    VALUES (@RevalRuleSetId, @CollegeId, 'Revaluation', 'REVAL', 1, @PatternId, @GradeMasterId, @Now, 0);
END
ELSE
BEGIN
    UPDATE RuleSet SET IsActive = 1, UpdatedAt = @Now WHERE RuleSetId = @RevalRuleSetId;
END

SELECT @RevalRuleId = r.RuleId
FROM [Rule] r
WHERE r.RuleSetId = @RevalRuleSetId AND r.Name = 'Allow revaluation' AND r.IsDeleted = 0;

IF @RevalRuleId IS NOT NULL
BEGIN
    DELETE FROM RuleCondition WHERE RuleId = @RevalRuleId;
    DELETE FROM RuleAction    WHERE RuleId = @RevalRuleId;
END
ELSE
BEGIN
    SET @RevalRuleId = NEWID();
    INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, RuleSetId, CreatedAt, IsDeleted)
    VALUES (@RevalRuleId, 'Allow revaluation', 10, 1, 0, @RevalRuleSetId, @Now, 0);
END

-- No RuleCondition rows: a rule with no conditions is vacuously true, so every student who
-- sat the source exam is offered.

INSERT INTO RuleAction (ActionId, ActionType, CalculationMode, Param1Type, Param1Value,
                        Param2Type, Param2Value, MaxLimit, MaxTargetCount, Target,
                        Expression, RuleId, CreatedAt, IsDeleted)
VALUES (NEWID(), 'AllowExamAssignment', 'Fixed', 'None', NULL,
        'None', NULL, NULL, 0, 'FailingSubjects,PassingSubjects,ESE',
        NULL, @RevalRuleId, @Now, 0);

-- ---------------------------------------------------------------------------------------
-- Further examples, for reference:
--
--   Cap a student at 4 backlog subjects per attempt:
--     UPDATE RuleAction SET MaxTargetCount = 4 WHERE RuleId = @AtktRuleId;
--
--   Only students carrying 4 or fewer backlogs may be assigned at all:
--     INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted)
--     VALUES (NEWID(), 'FailedSubjectCount', '<=', '4', @AtktRuleId, GETUTCDATE(), 0);
--
--   Re-sit theory only, keep term work (legacy parity):
--     UPDATE RuleAction SET Target = 'FailingSubjects,AbsentSubjects,ESE' WHERE RuleId = @AtktRuleId;
-- ---------------------------------------------------------------------------------------

SELECT rs.Name AS RuleSet, rs.ExamType, r.Name AS RuleName, r.Priority, r.IsEnabled,
       a.ActionType, a.Target, a.MaxTargetCount,
       (SELECT COUNT(*) FROM RuleCondition c WHERE c.RuleId = r.RuleId AND c.IsDeleted = 0) AS Conditions
FROM RuleSet rs
JOIN [Rule] r ON r.RuleSetId = rs.RuleSetId AND r.IsDeleted = 0
JOIN RuleAction a ON a.RuleId = r.RuleId AND a.IsDeleted = 0
WHERE rs.PatternId = @PatternId AND a.ActionType = 'AllowExamAssignment' AND rs.IsDeleted = 0
ORDER BY rs.ExamType, r.Priority;

COMMIT TRANSACTION;
