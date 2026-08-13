-- =======================================================================================
-- SeedExamAssignmentPolicies.sql
-- Seeds ExamAssignmentPolicy -- the configuration behind the ATKT / Revaluation
-- exam-assignment screen (the migration of legacy frm_atktreval_exm_assign.aspx).
--
-- WHY THIS TABLE EXISTS (and why the Ordinance rule engine could not hold this)
--   The ordinance engine evaluates IFactProvider(StudentMaster, MarksMaster) -> double. That
--   shape can express STUDENT-level eligibility ("at most 4 backlogs"), and this table links
--   to it through RuleSetId for exactly that purpose. It cannot express the other two things
--   this screen turns on:
--     * per-(student, SUBJECT) selectability -- no fact takes a subject argument, so the
--       engine cannot answer "may this student tick THIS subject";
--     * configuration with no student in it -- which ExamTypes are valid targets, which head
--       types are revaluable, whether a cleared subject may be re-registered.
--   Those are the literals the legacy code-behind hard-coded:
--     exam_code LIKE 'EXM%' AND atkt_exam = 1   -> TargetExamTypes
--     remark = 'UnSuccessful'                   -> RequireFailedSubject
--     Result = 'Pass' -> no checkbox            -> OfferPassedSubjects
--     h1/h2 contains 'Ab' -> no checkbox        -> BlockAbsentStudents
--     head type literal "ESE"                   -> EligibleHeadTypes
--     "marks already entered" delete guard      -> BlockDeleteAfterMarksEntry
--
-- TENANCY
--   CollegeId is NOT NULL -- there are no platform-wide rows. This script inserts one ATKT
--   policy and one Revaluation policy per college that does not already have them.
--   A college with no row still works: AtktRevalExamService falls back to built-in defaults
--   identical to the values below.
--
-- Idempotent: existing rows are refreshed in place, missing ones are created. Safe to re-run.
-- =======================================================================================

BEGIN TRANSACTION;
SET NOCOUNT ON;

DECLARE @Now DATETIME2 = GETUTCDATE();

-- ---------------------------------------------------------------------------------------
-- 1. ATKT (Allowed To Keep Term) -- the backlog / supplementary attempt.
--
--    RequireFailedSubject = 1   only subjects the student did not clear may be selected;
--                               the modelled form of remark = 'UnSuccessful'.
--    OfferPassedSubjects  = 0   a cleared subject cannot be re-registered.
--    BlockAbsentStudents  = 0   an absent student MAY sit the backlog exam.
--    CarryForwardMarks    = 1   subjects not being re-attempted are copied across with
--                               StudentMarks.IsCarryForward = 1 -- the replacement for the
--                               legacy trailing-'+' sentinel inside the h1/h2 strings.
--    EligibleHeadTypes    = ''  every head qualifies.
-- ---------------------------------------------------------------------------------------
INSERT INTO ExamAssignmentPolicy
    (PolicyId, CollegeId, Name, Mode, SourceExamTypes, TargetExamTypes,
     RequireFailedSubject, OfferPassedSubjects, BlockAbsentStudents, AutoSelectFailedSubjects,
     EligibleHeadTypes, MaxSubjectsPerStudent, CarryForwardSeatNo, CarryForwardMarks,
     BlockDeleteAfterMarksEntry, RuleSetId, PatternId, IsEnabled, SubjectsPerRow,
     CreatedAt, IsDeleted)
SELECT
    NEWID(), c.CollegeId, 'Standard ATKT assignment', 'ATKT',
    'Regular,ATKT,KT,Re-Exam', 'ATKT,KT,Re-Exam',
    1, 0, 0, 1,
    '', NULL, 1, 1,
    1, NULL, NULL, 1, 7,
    @Now, 0
FROM College c
WHERE c.IsDeleted = 0
  AND NOT EXISTS (
        SELECT 1 FROM ExamAssignmentPolicy p
        WHERE p.CollegeId = c.CollegeId AND p.Mode = 'ATKT' AND p.IsDeleted = 0);

UPDATE ExamAssignmentPolicy
SET Name                       = 'Standard ATKT assignment',
    SourceExamTypes            = 'Regular,ATKT,KT,Re-Exam',
    TargetExamTypes            = 'ATKT,KT,Re-Exam',
    RequireFailedSubject       = 1,
    OfferPassedSubjects        = 0,
    BlockAbsentStudents        = 0,
    AutoSelectFailedSubjects   = 1,
    EligibleHeadTypes          = '',
    CarryForwardSeatNo         = 1,
    CarryForwardMarks          = 1,
    BlockDeleteAfterMarksEntry = 1,
    IsEnabled                  = 1,
    SubjectsPerRow             = 7,
    UpdatedAt                  = @Now
WHERE Mode = 'ATKT' AND IsDeleted = 0;

-- ---------------------------------------------------------------------------------------
-- 2. Revaluation -- re-marking an already-conducted attempt.
--
--    RequireFailedSubject = 0   pass or fail, any subject may be sent for revaluation.
--    BlockAbsentStudents  = 1   an absent student has no answer book to revalue. (Legacy
--                               comment: "checking only H1 Ab as college told they dont
--                               make absent student in h2".)
--    EligibleHeadTypes = 'ESE'  only the end-semester head is revaluable; internal heads
--                               are not. Clear this column to allow every head.
--    AutoSelectFailedSubjects = 0   revaluation is always an explicit, per-student choice.
--
--    The target exam is not chosen freely: it is the ExamMaster row whose
--    RevaluationForExamId points at the selected source exam (the modelled form of the
--    legacy 'R' + exam_code convention), so TargetExamTypes is not consulted for this mode.
-- ---------------------------------------------------------------------------------------
INSERT INTO ExamAssignmentPolicy
    (PolicyId, CollegeId, Name, Mode, SourceExamTypes, TargetExamTypes,
     RequireFailedSubject, OfferPassedSubjects, BlockAbsentStudents, AutoSelectFailedSubjects,
     EligibleHeadTypes, MaxSubjectsPerStudent, CarryForwardSeatNo, CarryForwardMarks,
     BlockDeleteAfterMarksEntry, RuleSetId, PatternId, IsEnabled, SubjectsPerRow,
     CreatedAt, IsDeleted)
SELECT
    NEWID(), c.CollegeId, 'Standard Revaluation assignment', 'Revaluation',
    'Regular,ATKT,KT,Re-Exam', '',
    0, 1, 1, 0,
    'ESE', NULL, 1, 1,
    1, NULL, NULL, 1, 7,
    @Now, 0
FROM College c
WHERE c.IsDeleted = 0
  AND NOT EXISTS (
        SELECT 1 FROM ExamAssignmentPolicy p
        WHERE p.CollegeId = c.CollegeId AND p.Mode = 'Revaluation' AND p.IsDeleted = 0);

UPDATE ExamAssignmentPolicy
SET Name                       = 'Standard Revaluation assignment',
    SourceExamTypes            = 'Regular,ATKT,KT,Re-Exam',
    TargetExamTypes            = '',
    RequireFailedSubject       = 0,
    OfferPassedSubjects        = 1,
    BlockAbsentStudents        = 1,
    AutoSelectFailedSubjects   = 0,
    EligibleHeadTypes          = 'ESE',
    CarryForwardSeatNo         = 1,
    CarryForwardMarks          = 1,
    BlockDeleteAfterMarksEntry = 1,
    IsEnabled                  = 1,
    SubjectsPerRow             = 7,
    UpdatedAt                  = @Now
WHERE Mode = 'Revaluation' AND IsDeleted = 0;

-- ---------------------------------------------------------------------------------------
-- 3. OPTIONAL: gate ATKT eligibility with an ordinance rule set.
--
--    ExamAssignmentPolicy.RuleSetId points at a RuleSet whose enabled rules must ALL hold
--    for a student to be listed. Conditions are evaluated by the same engine that runs
--    result processing (RuleConditionEvaluator + the IFactProvider registry), so any fact
--    already registered can be used: FailedSubjectCount, FailedHeadCount, SemesterNo,
--    YearlyPercentageCreditsEarned, IsATKT, HasQuota, Percentage, ...
--
--    Example -- "a student carrying more than 4 backlog subjects may not be assigned":
--
--      DECLARE @CollegeId UNIQUEIDENTIFIER = '103EBF99-FEB0-43BC-A312-56FE85D3BCC6';
--      DECLARE @PatternId UNIQUEIDENTIFIER =
--          (SELECT PatternId FROM PatternMaster WHERE PatternName = 'NEP' AND IsDeleted = 0);
--      DECLARE @RuleSetId UNIQUEIDENTIFIER = NEWID();
--      DECLARE @RuleId    UNIQUEIDENTIFIER = NEWID();
--
--      INSERT INTO RuleSet (RuleSetId, CollegeId, Name, ExamType, IsActive, PatternId, CreatedAt, IsDeleted)
--      VALUES (@RuleSetId, @CollegeId, 'ATKT eligibility', 'ATKT', 1, @PatternId, GETUTCDATE(), 0);
--
--      INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, RuleSetId, CreatedAt, IsDeleted)
--      VALUES (@RuleId, 'At most 4 backlogs', 1, 1, 0, @RuleSetId, GETUTCDATE(), 0);
--
--      INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted)
--      VALUES (NEWID(), 'FailedSubjectCount', '<=', '4', @RuleId, GETUTCDATE(), 0);
--
--      UPDATE ExamAssignmentPolicy SET RuleSetId = @RuleSetId
--      WHERE CollegeId = @CollegeId AND Mode = 'ATKT' AND IsDeleted = 0;
-- ---------------------------------------------------------------------------------------

SELECT p.PolicyId, c.Name AS College, p.Name, p.Mode, p.TargetExamTypes, p.EligibleHeadTypes,
       p.RequireFailedSubject, p.OfferPassedSubjects, p.BlockAbsentStudents, p.IsEnabled
FROM ExamAssignmentPolicy p
JOIN College c ON c.CollegeId = p.CollegeId
WHERE p.IsDeleted = 0
ORDER BY c.Name, p.Mode;

COMMIT TRANSACTION;
