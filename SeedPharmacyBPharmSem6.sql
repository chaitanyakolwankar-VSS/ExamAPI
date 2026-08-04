-- =======================================================================================
-- SeedPharmacyBPharmSem6.sql
-- B.Pharm Semester-VI dataset for the pharmacy college PHM001: PCI grade scale, the single
-- '@' grace rule, 9 subjects (2 heads each, Combined passing), 30 students and their marks.
--
-- WHY:
--   The PCI CBCS regulation (Syllabus_B_Pharm2016-17, Sec.12) passes a course on the COMBINED
--   total of internal + end-semester, with NO minimum on either component. That is exactly
--   what PassingStrategy='Combined' + PassPercentage=50 expresses, so every subject here is
--   Combined/50 and every head carries HeadPass='0' -- a per-head minimum would contradict
--   the regulation.
--
--   Subjects, credits and the marks scheme come from the PDF: Table VI (subjects + credit
--   points) and Table X (75+25=100 theory, 35+15=50 practical, aggregate 750). Table IX
--   prints 26 credits for Sem-VI, which is a defect in the source document -- Table VI's own
--   rows sum to 30 (6x4 theory + 3x2 practical) and 30 is what is seeded.
--
--   The PDF defines NO grace of any kind (searched: grace/condone/condonation -> nothing but
--   a re-admission FEE in Sec.27). The '@' rule below is therefore the COLLEGE'S OWN POLICY,
--   not PCI and not a Mumbai ordinance: a flat 2 marks per failing subject, applied to any
--   number of failing subjects. It is combined-aware -- AddGraceHandler awards against the
--   subject deficit on StudentSubjectResult, so a subject within 2 of the 50% line is lifted
--   to exactly 50% and grades D.
--
--   All Mumbai-cloned content that ProvisionPharmacyCollege.sql copied from engineering is
--   removed here: the 8-grade engineering scale is replaced by the PDF's 6-grade scale, and
--   the 6 cloned ordinance rules (O.5045-A / O.5042-A / O.229 / O.5043-A / O.5044-A) are
--   soft-deleted and replaced by the single '@' rule. The GradeMaster and RuleSet ROWS are
--   reused rather than recreated: ProvisionPharmacyCollege.sql guards on "any RuleSet for
--   this college", so deleting the row outright would make that script re-clone the Mumbai
--   rules on its next run.
--
-- Depends on ProvisionPharmacyCollege.sql having run. College PHM001, its AcademicYear rows,
-- CourseMaster BPHARM, PatternMaster 'NEP', GradeMaster, RuleSet, RoleMaster and the admin
-- user already exist and are NOT re-created here.
--
-- Reads (never writes) the engineering college to copy 30 student names. The copies are
-- independent PHM001 rows with fresh GUIDs and their own PH25xx roll numbers.
--
-- Does NOT seed StudentSubjectResult -- result processing computes it -- and does NOT seed
-- ResolutionMaster.
--
-- Idempotent: keyed on natural keys (CollegeCode, SubjectCode, StudentId, ExamType+Semester,
-- Grade letter, Rule name). Re-running restores the pristine seed state, including resetting
-- marks that a previous processing run may have altered. Safe to re-run.
-- =======================================================================================

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;   -- required: filtered unique indexes exist on StudentMaster

BEGIN TRANSACTION;

DECLARE @Now          DATETIME2      = GETUTCDATE();
DECLARE @CollegeCode  NVARCHAR(20)   = 'PHM001';
DECLARE @CourseCode   NVARCHAR(20)   = 'BPHARM';
DECLARE @PatternName  NVARCHAR(100)  = 'NEP';
DECLARE @ExamType     NVARCHAR(50)   = 'Regular';
DECLARE @ExamName     NVARCHAR(100)  = 'B.Pharm Sem-VI Regular Exam';
DECLARE @SemId        NVARCHAR(20)   = 'Sem-6';
DECLARE @SemName      NVARCHAR(50)   = 'Semester VI';
DECLARE @PassPct      INT            = 50;     -- PCI Sec.12: 50% of the combined course total
DECLARE @GraceMarks   DECIMAL(18,2)  = 2.00;   -- college policy: flat 2 marks per failing subject
DECLARE @GraceSymbol  NVARCHAR(10)   = '@';
DECLARE @RuleName     NVARCHAR(100)  = 'Pharmacy Grace (@) - 2 marks per failing subject';
DECLARE @GradeName    NVARCHAR(100)  = 'PCI B.Pharm Grade Scale (CBCS 2016)';
DECLARE @RuleSetName  NVARCHAR(100)  = 'B.Pharm Regular (PCI CBCS)';
DECLARE @StudentCount INT            = 30;

-- Source of the copied student names. Read-only; nothing in this script writes to it.
DECLARE @EnggCollegeId UNIQUEIDENTIFIER = '103EBF99-FEB0-43BC-A312-56FE85D3BCC6';

DECLARE @CollegeId     UNIQUEIDENTIFIER,
        @CourseId      UNIQUEIDENTIFIER,
        @PatternId     UNIQUEIDENTIFIER,
        @AyId          UNIQUEIDENTIFIER,
        @GradeMasterId UNIQUEIDENTIFIER,
        @RuleSetId     UNIQUEIDENTIFIER,
        @ExamId        UNIQUEIDENTIFIER,
        @RuleId        UNIQUEIDENTIFIER;

-- ---------------------------------------------------------------------------------------
-- 0. Resolve the shell provisioned by ProvisionPharmacyCollege.sql.
--    Abort rather than invent rows: a dangling CollegeId produces data the EF global query
--    filter hides from the API forever.
-- ---------------------------------------------------------------------------------------
SELECT @CollegeId = CollegeId FROM College
WHERE CollegeCode = @CollegeCode AND IsDeleted = 0;
IF @CollegeId IS NULL
BEGIN
    RAISERROR('College %s not found. Run ProvisionPharmacyCollege.sql first.', 16, 1, @CollegeCode);
    ROLLBACK TRANSACTION; RETURN;
END

SELECT @CourseId = CourseId FROM CourseMaster
WHERE CollegeId = @CollegeId AND CourseCode = @CourseCode AND IsDeleted = 0;
IF @CourseId IS NULL
BEGIN
    RAISERROR('CourseMaster %s not found for PHM001.', 16, 1, @CourseCode);
    ROLLBACK TRANSACTION; RETURN;
END

SELECT @PatternId = PatternId FROM PatternMaster
WHERE CollegeId = @CollegeId AND PatternName = @PatternName AND IsDeleted = 0;
IF @PatternId IS NULL
BEGIN
    RAISERROR('PatternMaster %s not found for PHM001.', 16, 1, @PatternName);
    ROLLBACK TRANSACTION; RETURN;
END

SELECT @AyId = AYID FROM AcademicYear
WHERE CollegeId = @CollegeId AND IsCurrent = 1 AND IsDeleted = 0;
IF @AyId IS NULL
BEGIN
    RAISERROR('No current AcademicYear for PHM001.', 16, 1);
    ROLLBACK TRANSACTION; RETURN;
END

SELECT @GradeMasterId = GradeMasterId FROM GradeMaster
WHERE CollegeId = @CollegeId AND IsDeleted = 0;
IF @GradeMasterId IS NULL
BEGIN
    RAISERROR('No GradeMaster for PHM001.', 16, 1);
    ROLLBACK TRANSACTION; RETURN;
END

SELECT @RuleSetId = RuleSetId FROM RuleSet
WHERE CollegeId = @CollegeId AND PatternId = @PatternId AND IsDeleted = 0;
IF @RuleSetId IS NULL
BEGIN
    RAISERROR('No RuleSet for PHM001.', 16, 1);
    ROLLBACK TRANSACTION; RETURN;
END

IF (SELECT COUNT(*) FROM StudentMaster WHERE CollegeId = @EnggCollegeId AND IsDeleted = 0) < @StudentCount
BEGIN
    RAISERROR('Engineering college has fewer than %d students to copy.', 16, 1, @StudentCount);
    ROLLBACK TRANSACTION; RETURN;
END

-- ---------------------------------------------------------------------------------------
-- 1. Grade scale -- replace the cloned Mumbai 8-grade scale with the PDF's 6-grade scale.
--    PDF Table XII: O 90+/10, A 80/9, B 70/8, C 60/7, D 50/6, F <50/0.
--    There is no AB threshold: GetGradePointFromPercentage looks up by percentage only, so
--    an absent subject scores 0% -> F with GradePoint 0, and StudentSubjectResult.
--    SubjectStatus carries 'Absent' (set from IsAbsent) as the real absence signal.
-- ---------------------------------------------------------------------------------------
UPDATE GradeMaster
SET Name = @GradeName,
    Description = 'PCI B.Pharm CBCS 2016 grade scale (Table XII). Replaces the cloned Mumbai scale.',
    UpdatedAt = @Now
WHERE GradeMasterId = @GradeMasterId;

DECLARE @Grades TABLE (Grade NVARCHAR(10) PRIMARY KEY, GradePoint INT,
                       MinPct DECIMAL(5,2), MaxPct DECIMAL(5,2), Remark NVARCHAR(50));
INSERT INTO @Grades (Grade, GradePoint, MinPct, MaxPct, Remark) VALUES
 ('O', 10, 90.00, 100.00, 'Outstanding'),
 ('A',  9, 80.00,  89.99, 'Excellent'),
 ('B',  8, 70.00,  79.99, 'Good'),
 ('C',  7, 60.00,  69.99, 'Fair'),
 ('D',  6, 50.00,  59.99, 'Average'),
 ('F',  0,  0.00,  49.99, 'Fail');

-- Retire any threshold that is not part of the PCI scale (the cloned E / P bands, and any
-- letter whose boundaries were the Mumbai ones).
UPDATE gt SET IsDeleted = 1, DeletedAt = @Now
FROM GradeThreshold gt
WHERE gt.GradeMasterId = @GradeMasterId AND gt.IsDeleted = 0
  AND NOT EXISTS (SELECT 1 FROM @Grades g WHERE g.Grade = gt.Grade);

-- Re-point the surviving letters onto the PCI boundaries, then add any that are missing.
UPDATE gt
SET GradePoint = g.GradePoint, MinPercentage = g.MinPct, MaxPercentage = g.MaxPct,
    PerformanceRemark = g.Remark, UpdatedAt = @Now
FROM GradeThreshold gt
JOIN @Grades g ON g.Grade = gt.Grade
WHERE gt.GradeMasterId = @GradeMasterId AND gt.IsDeleted = 0;

INSERT INTO GradeThreshold (ThresholdId, Grade, GradePoint, MinPercentage, MaxPercentage,
                            PerformanceRemark, GradeMasterId, CreatedAt, IsDeleted)
SELECT NEWID(), g.Grade, g.GradePoint, g.MinPct, g.MaxPct, g.Remark, @GradeMasterId, @Now, 0
FROM @Grades g
WHERE NOT EXISTS (SELECT 1 FROM GradeThreshold gt
                  WHERE gt.GradeMasterId = @GradeMasterId AND gt.Grade = g.Grade AND gt.IsDeleted = 0);

-- ---------------------------------------------------------------------------------------
-- 2. RuleSet -- drop the cloned Mumbai ordinances, leave exactly one rule: the '@' grace.
--
--    Action shape, verified against AddGraceHandler:
--      Target='FailingSubjects'  -> IsAllFailingHeadsTarget() is true, which is the ONLY
--                                   target form that can grace a Combined subject.
--      Param1Type='Absolute',
--      Param1Value=3, CalculationMode='Fixed'
--                                -> allowance per subject is a flat 2 marks.
--      Param2Type='None'         -> limit2 is unbounded; 'Fixed' ignores it anyway.
--      MaxLimit=9999             -> MaxLimit is the TOTAL pool across all subjects and falls
--                                   back to Param1Value when NULL, which would cap the whole
--                                   student at 2 marks. Set high so the only real cap is the
--                                   flat 2-per-subject.
--      MaxTargetCount=0          -> unlimited number of failing subjects (0 = no cap).
--      Expression=NULL           -> a non-empty Expression would OVERRIDE the flat 2 with an
--                                   NCalc grace chart, which is the Mumbai behaviour we are
--                                   removing.
--    Grace is all-or-nothing per subject: a subject short by more than 3 receives nothing.
-- ---------------------------------------------------------------------------------------
UPDATE RuleSet
SET Name = @RuleSetName, ExamType = @ExamType, IsActive = 1,
    GradeMasterId = @GradeMasterId, UpdatedAt = @Now
WHERE RuleSetId = @RuleSetId;

-- Scope every cleanup to this RuleSet. SeedOrdinances.sql wipes these tables globally;
-- doing that here would destroy the engineering college's rules.
UPDATE ra SET IsDeleted = 1, DeletedAt = @Now
FROM RuleAction ra
JOIN [Rule] r ON r.RuleId = ra.RuleId
WHERE r.RuleSetId = @RuleSetId AND r.Name <> @RuleName AND ra.IsDeleted = 0;

UPDATE rc SET IsDeleted = 1, DeletedAt = @Now
FROM RuleCondition rc
JOIN [Rule] r ON r.RuleId = rc.RuleId
WHERE r.RuleSetId = @RuleSetId AND r.Name <> @RuleName AND rc.IsDeleted = 0;

UPDATE [Rule] SET IsDeleted = 1, IsEnabled = 0, DeletedAt = @Now
WHERE RuleSetId = @RuleSetId AND Name <> @RuleName AND IsDeleted = 0;

SELECT @RuleId = RuleId FROM [Rule]
WHERE RuleSetId = @RuleSetId AND Name = @RuleName AND IsDeleted = 0;

IF @RuleId IS NULL
BEGIN
    SET @RuleId = NEWID();
    INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, OrdinanceSymbol,
                        RuleSetId, CreatedAt, IsDeleted)
    VALUES (@RuleId, @RuleName, 1, 1, 0, @GraceSymbol, @RuleSetId, @Now, 0);
END
ELSE
    UPDATE [Rule] SET Priority = 1, IsEnabled = 1, StopOnSuccess = 0,
                      OrdinanceSymbol = @GraceSymbol, UpdatedAt = @Now
    WHERE RuleId = @RuleId;

-- FailedSubjectCount (not FailedHeadCount): for a Combined subject the two facts are equal,
-- but the subject-level one is what this rule actually means.
-- Operator is the SYMBOL form '>=', not 'GreaterOrEqual'. CompareValues accepts both, but the
-- Ordinance editor's operator list (served by GetEngineMetadataAsync) only offers symbols, so
-- the word form cannot round-trip through the UI.
IF NOT EXISTS (SELECT 1 FROM RuleCondition
               WHERE RuleId = @RuleId AND FactName = 'FailedSubjectCount' AND IsDeleted = 0)
    INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted)
    VALUES (NEWID(), 'FailedSubjectCount', '>=', '1', @RuleId, @Now, 0);
ELSE
    UPDATE RuleCondition SET Operator = '>=', Value = '1', UpdatedAt = @Now
    WHERE RuleId = @RuleId AND FactName = 'FailedSubjectCount' AND IsDeleted = 0;

IF NOT EXISTS (SELECT 1 FROM RuleAction
               WHERE RuleId = @RuleId AND ActionType = 'AddGrace' AND IsDeleted = 0)
    INSERT INTO RuleAction (ActionId, ActionType, CalculationMode, Param1Type, Param1Value,
                            Param2Type, Param2Value, MaxLimit, MaxTargetCount, Target,
                            Expression, RuleId, CreatedAt, IsDeleted)
    VALUES (NEWID(), 'AddGrace', 'Fixed', 'Absolute', @GraceMarks, 'None', NULL,
            9999.00, 0, 'FailingSubjects', NULL, @RuleId, @Now, 0);
ELSE
    UPDATE RuleAction
    SET CalculationMode = 'Fixed', Param1Type = 'Absolute', Param1Value = @GraceMarks,
        Param2Type = 'None', Param2Value = NULL, MaxLimit = 9999.00, MaxTargetCount = 0,
        Target = 'FailingSubjects', Expression = NULL, UpdatedAt = @Now
    WHERE RuleId = @RuleId AND ActionType = 'AddGrace' AND IsDeleted = 0;

-- GraceLookup is reference data only -- no service reads it (the live chart, when there is
-- one, lives in RuleAction.Expression). The flat 2-mark policy needs no chart, so PHM001
-- deliberately has none. Retire any that a previous run left behind.
UPDATE GraceLookup SET IsDeleted = 1, DeletedAt = @Now
WHERE CollegeId = @CollegeId AND IsDeleted = 0;

-- ---------------------------------------------------------------------------------------
-- 3. Exam. ExamMaster has no PatternId -- the pattern travels as a string on
--    MarksMaster.Pattern and SubjectMaster.Pattern, and that is what ResultService matches.
-- ---------------------------------------------------------------------------------------
SELECT @ExamId = ExamId FROM ExamMaster
WHERE CollegeId = @CollegeId AND CourseId = @CourseId
  AND ExamType = @ExamType AND Semester = @SemId AND IsDeleted = 0;

IF @ExamId IS NULL
BEGIN
    SET @ExamId = NEWID();
    INSERT INTO ExamMaster (ExamId, Name, ExamType, Semester, CourseId, AcademicYearAYID,
                            CollegeId, IsActive, IsLocked, CreatedAt, IsDeleted)
    VALUES (@ExamId, @ExamName, @ExamType, @SemId, @CourseId, @AyId,
            @CollegeId, 1, 0, @Now, 0);
END
ELSE
    -- A locked exam refuses result processing; keep the demo exam runnable.
    UPDATE ExamMaster SET Name = @ExamName, IsActive = 1, IsLocked = 0, UpdatedAt = @Now
    WHERE ExamId = @ExamId;

-- ---------------------------------------------------------------------------------------
-- 4. Subjects, credits and heads -- PDF Table VI (titles + credit points) and Table X
--    (marks scheme). Theory and practical are SEPARATE subjects with their own codes.
--
--    Head is the positional key ('H1'/'H2') that SubjectPassEvaluator.FindCredit matches
--    against StudentMarks.Head; HeadType is the printed label. They must agree exactly or
--    every head silently resolves to out-of 0.
--
--    HeadPass is '0' on purpose: PCI Sec.12 sets no minimum on the internal or the
--    end-semester component alone. The only threshold is PassPercentage=50 on the subject.
-- ---------------------------------------------------------------------------------------
DECLARE @Subjects TABLE (SubIdx INT PRIMARY KEY, SubjectCode NVARCHAR(20), Name NVARCHAR(100),
                         Credits NVARCHAR(20), EsaOutOf INT, TwOutOf INT, TotalOutOf INT);
INSERT INTO @Subjects (SubIdx, SubjectCode, Name, Credits, EsaOutOf, TwOutOf, TotalOutOf) VALUES
 (1, 'BP601T', 'Medicinal Chemistry III - Theory',                4, 75, 25, 100),
 (2, 'BP602T', 'Pharmacology III - Theory',                       4, 75, 25, 100),
 (3, 'BP603T', 'Herbal Drug Technology - Theory',                 4, 75, 25, 100),
 (4, 'BP604T', 'Biopharmaceutics and Pharmacokinetics - Theory',  4, 75, 25, 100),
 (5, 'BP605T', 'Pharmaceutical Biotechnology - Theory',           4, 75, 25, 100),
 (6, 'BP606T', 'Quality Assurance - Theory',                      4, 75, 25, 100),
 (7, 'BP607P', 'Medicinal Chemistry III - Practical',             2, 35, 15,  50),
 (8, 'BP608P', 'Pharmacology III - Practical',                    2, 35, 15,  50),
 (9, 'BP609P', 'Herbal Drug Technology - Practical',              2, 35, 15,  50);

-- SubjectCreditMaster.AYID is an nvarchar holding a LOWERCASE guid string, and the typed
-- AcademicYearAYID is left NULL. That is the convention every existing row follows.
DECLARE @AyIdText NVARCHAR(50) = LOWER(CONVERT(NVARCHAR(50), @AyId));

DECLARE @SubjectMap TABLE (SubIdx INT PRIMARY KEY, SubjectId UNIQUEIDENTIFIER,
                           CreditsId UNIQUEIDENTIFIER);

INSERT INTO SubjectMaster (SubjectId, SubjectCode, Name, SemId, SemName, Pattern,
                           CourseId, CollegeId, CreatedAt, IsDeleted)
SELECT NEWID(), s.SubjectCode, s.Name, @SemId, @SemName, @PatternName,
       @CourseId, @CollegeId, @Now, 0
FROM @Subjects s
WHERE NOT EXISTS (SELECT 1 FROM SubjectMaster sm
                  WHERE sm.CollegeId = @CollegeId AND sm.SubjectCode = s.SubjectCode
                    AND sm.IsDeleted = 0);

INSERT INTO @SubjectMap (SubIdx, SubjectId)
SELECT s.SubIdx, sm.SubjectId
FROM @Subjects s
JOIN SubjectMaster sm ON sm.SubjectCode = s.SubjectCode
                     AND sm.CollegeId = @CollegeId AND sm.IsDeleted = 0;

INSERT INTO SubjectCreditMaster (CreditsId, TotalCredits, AYID, AcademicYearAYID,
                                 PassingStrategy, PassPercentage, SubjectId,
                                 CollegeId, CreatedAt, IsDeleted)
SELECT NEWID(), s.Credits, @AyIdText, NULL, 'Combined', @PassPct, m.SubjectId,
       @CollegeId, @Now, 0
FROM @Subjects s
JOIN @SubjectMap m ON m.SubIdx = s.SubIdx
WHERE NOT EXISTS (SELECT 1 FROM SubjectCreditMaster scm
                  WHERE scm.SubjectId = m.SubjectId AND scm.IsDeleted = 0);

UPDATE m SET CreditsId = scm.CreditsId
FROM @SubjectMap m
JOIN SubjectCreditMaster scm ON scm.SubjectId = m.SubjectId AND scm.IsDeleted = 0;

-- Re-assert the config on re-runs, so a hand-edit in the UI does not silently persist.
UPDATE scm
SET TotalCredits = s.Credits, PassingStrategy = 'Combined', PassPercentage = @PassPct,
    AYID = @AyIdText, UpdatedAt = @Now
FROM SubjectCreditMaster scm
JOIN @SubjectMap m ON m.CreditsId = scm.CreditsId
JOIN @Subjects s ON s.SubIdx = m.SubIdx;

DECLARE @Heads TABLE (SubIdx INT, Head NVARCHAR(50), HeadType NVARCHAR(50),
                      HeadOutOf NVARCHAR(20), PRIMARY KEY (SubIdx, Head));
INSERT INTO @Heads (SubIdx, Head, HeadType, HeadOutOf)
SELECT SubIdx, 'H1', 'ESA', CONVERT(NVARCHAR(20), EsaOutOf) FROM @Subjects
UNION ALL
SELECT SubIdx, 'H2', 'TW',  CONVERT(NVARCHAR(20), TwOutOf)  FROM @Subjects;

INSERT INTO SubjectCredits (Id, Head, HeadType, HeadOutOf, HeadPass, HeadResolution,
                            HeadFormula, CreditsId, CreatedAt, IsDeleted)
SELECT NEWID(), h.Head, h.HeadType, h.HeadOutOf, '0', NULL,
       CONVERT(NVARCHAR(100), @PassPct), m.CreditsId, @Now, 0
FROM @Heads h
JOIN @SubjectMap m ON m.SubIdx = h.SubIdx
WHERE NOT EXISTS (SELECT 1 FROM SubjectCredits sc
                  WHERE sc.CreditsId = m.CreditsId AND sc.Head = h.Head AND sc.IsDeleted = 0);

UPDATE sc
SET HeadType = h.HeadType, HeadOutOf = h.HeadOutOf, HeadPass = '0',
    HeadFormula = CONVERT(NVARCHAR(100), @PassPct), UpdatedAt = @Now
FROM SubjectCredits sc
JOIN @SubjectMap m ON m.CreditsId = sc.CreditsId
JOIN @Heads h ON h.SubIdx = m.SubIdx AND h.Head = sc.Head
WHERE sc.IsDeleted = 0;

-- ---------------------------------------------------------------------------------------
-- 5. Students -- 30 names copied from the engineering roster into INDEPENDENT PHM001 rows.
--    Fresh GUIDs, PHM001 CollegeId, and their own PH25xx roll numbers so a pharmacy seat
--    number can never collide with an engineering one (SeedOrdinances.sql contains an
--    un-scoped UPDATE keyed on the 6242xxx seat numbers).
-- ---------------------------------------------------------------------------------------
DECLARE @Roster TABLE (Idx INT PRIMARY KEY, StudentId NVARCHAR(50),
                       FirstName NVARCHAR(50), MiddleName NVARCHAR(50), LastName NVARCHAR(50),
                       StdMstId UNIQUEIDENTIFIER);

INSERT INTO @Roster (Idx, StudentId, FirstName, MiddleName, LastName)
SELECT TOP (@StudentCount)
       ROW_NUMBER() OVER (ORDER BY src.StudentId),
       'PH25' + RIGHT('00' + CONVERT(NVARCHAR(4), ROW_NUMBER() OVER (ORDER BY src.StudentId)), 2),
       src.FirstName, src.MiddleName, src.LastName
FROM StudentMaster src
WHERE src.CollegeId = @EnggCollegeId AND src.IsDeleted = 0
ORDER BY src.StudentId;

INSERT INTO StudentMaster (StdMstId, StudentId, FirstName, MiddleName, LastName,
                           CollegeId, DyslexiaStudent, CreatedAt, IsDeleted)
SELECT NEWID(), r.StudentId, r.FirstName, r.MiddleName, r.LastName,
       @CollegeId, 0, @Now, 0
FROM @Roster r
WHERE NOT EXISTS (SELECT 1 FROM StudentMaster sm
                  WHERE sm.CollegeId = @CollegeId AND sm.StudentId = r.StudentId
                    AND sm.IsDeleted = 0);

UPDATE r SET StdMstId = sm.StdMstId
FROM @Roster r
JOIN StudentMaster sm ON sm.CollegeId = @CollegeId AND sm.StudentId = r.StudentId
                     AND sm.IsDeleted = 0;

-- StudentEligibility is mandatory: Student Master search and the marks-entry roster both
-- INNER JOIN it, so a student without this row is invisible to the API.
INSERT INTO StudentEligibility (Id, StdMstId, StudentId, CourseId, SemesterId, Pattern,
                                AYID, CollegeId, CreatedAt, IsDeleted)
SELECT NEWID(), r.StdMstId, r.StudentId, @CourseId, @SemId, @PatternName,
       @AyId, @CollegeId, @Now, 0
FROM @Roster r
WHERE NOT EXISTS (SELECT 1 FROM StudentEligibility se
                  WHERE se.StdMstId = r.StdMstId AND se.AYID = @AyId
                    AND se.SemesterId = @SemId AND se.IsDeleted = 0);

-- ---------------------------------------------------------------------------------------
-- 6. Marks.
--
--    The dataset exists to make the '@' grace visible, so marks are engineered rather than
--    random. Every subject total is planned explicitly:
--      - a deterministic baseline per student (BasePct +/- a per-subject wobble), then
--      - @Overrides pins the interesting subjects onto exact totals.
--
--    Combined pass = CEILING(TotalOutOf * 50 / 100) -> 50 for theory, 25 for practical.
--    Deficit 1..3 -> the rule lifts the subject to exactly the pass mark -> 50% -> grade D.
--    Deficit 4+   -> nothing is awarded (grace is all-or-nothing per subject).
--
--    The split puts the weakness in ESA and keeps TW strong, which is both realistic and the
--    point of combined passing: these students would fail a head-wise engine on ESA alone.
-- ---------------------------------------------------------------------------------------
DECLARE @Profiles TABLE (Idx INT PRIMARY KEY, BasePct INT, Note NVARCHAR(60));
INSERT INTO @Profiles (Idx, BasePct, Note) VALUES
 ( 1, 78, 'clear pass'),            ( 2, 71, 'clear pass'),
 ( 3, 65, 'clear pass'),            ( 4, 60, 'clear pass'),
 ( 5, 62, 'graced x2'),             ( 6, 62, 'graced x1'),
 ( 7, 62, 'graced x2'),             ( 8, 62, 'graced x1 practical'),
 ( 9, 62, 'graced x3'),             (10, 62, 'graced x1'),
 (11, 62, 'graced x1 practical'),   (12, 62, 'graced x2'),
 (13, 62, 'graced x1'),             (14, 62, 'graced x2'),
 (15, 62, 'graced x4'),             (16, 62, 'graced x1 practical'),
 (17, 61, 'graced + fail'),         (18, 61, 'graced + fail'),
 (19, 61, 'graced + fail'),         (20, 61, 'graced + fail'),
 (21, 61, 'graced + fail'),         (22, 63, 'fail beyond grace'),
 (23, 63, 'fail beyond grace x2'),  (24, 63, 'fail beyond grace'),
 (25, 63, 'fail by 4 - just outside grace'),
 (26, 63, 'fail by 4 - just outside grace'),
 (27, 64, 'absent x1'),             (28, 64, 'absent x1 + graced x1'),
 (29, 93, 'topper'),                (30, 85, 'high performer');

-- (Idx, SubIdx, Total, IsAbsent). Totals are SUBJECT totals across both heads.
DECLARE @Overrides TABLE (Idx INT, SubIdx INT, Total INT, IsAbsent BIT, PRIMARY KEY (Idx, SubIdx));
INSERT INTO @Overrides (Idx, SubIdx, Total, IsAbsent) VALUES
 -- graced to pass: theory deficit 1-3 (pass 50), practical deficit 1-3 (pass 25)
 ( 5, 1, 49, 0), ( 5, 3, 48, 0),
 ( 6, 2, 47, 0),
 ( 7, 4, 48, 0), ( 7, 7, 24, 0),
 ( 8, 7, 22, 0),
 ( 9, 1, 47, 0), ( 9, 2, 48, 0), ( 9, 3, 49, 0),
 (10, 5, 49, 0),
 (11, 8, 23, 0),
 (12, 6, 47, 0), (12, 9, 24, 0),
 (13, 2, 49, 0),
 (14, 3, 47, 0), (14, 7, 23, 0),
 (15, 4, 49, 0), (15, 5, 48, 0), (15, 6, 47, 0), (15, 8, 24, 0),
 (16, 9, 22, 0),
 -- graced on one subject, failing beyond grace on another
 (17, 1, 48, 0), (17, 2, 44, 0),
 (18, 7, 24, 0), (18, 3, 41, 0),
 (19, 5, 47, 0), (19, 9, 20, 0),
 (20, 2, 49, 0), (20, 4, 45, 0),
 (21, 6, 48, 0), (21, 8, 19, 0),
 -- failing beyond grace, nothing awarded
 (22, 1, 44, 0),
 (23, 3, 40, 0), (23, 4, 43, 0),
 (24, 7, 20, 0),
 (25, 2, 46, 0),   -- deficit 4: one mark outside the grace window
 (26, 8, 21, 0),   -- deficit 4: one mark outside the grace window
 -- absent (both heads); grace cannot reach a 50-mark deficit
 (27, 4,  0, 1),
 (28, 9,  0, 1), (28, 1, 48, 0);

DECLARE @Plan TABLE (Idx INT, SubIdx INT, Total INT, IsAbsent BIT,
                     EsaMarks INT, TwMarks INT, PRIMARY KEY (Idx, SubIdx));

INSERT INTO @Plan (Idx, SubIdx, Total, IsAbsent, EsaMarks, TwMarks)
SELECT p.Idx, s.SubIdx,
       t.Total,
       ISNULL(o.IsAbsent, 0),
       0, 0
FROM @Profiles p
CROSS JOIN @Subjects s
LEFT JOIN @Overrides o ON o.Idx = p.Idx AND o.SubIdx = s.SubIdx
CROSS APPLY (
    SELECT Total = ISNULL(
        o.Total,
        CONVERT(INT, ROUND(s.TotalOutOf *
            (p.BasePct + ((p.Idx * 7 + s.SubIdx * 13) % 11) - 5) / 100.0, 0)))
) t;

-- Split the subject total into ESA + TW: internals stay strong, the shortfall lands on ESA.
UPDATE pl
SET TwMarks = CASE
        WHEN s.TotalOutOf = 100 THEN CASE WHEN pl.Total >= 60 THEN 20
                                          WHEN pl.Total >= 45 THEN 18
                                          WHEN pl.Total >= 30 THEN 15
                                          ELSE 10 END
        ELSE                          CASE WHEN pl.Total >= 30 THEN 12
                                          WHEN pl.Total >= 22 THEN 11
                                          WHEN pl.Total >= 15 THEN 9
                                          ELSE 6 END
    END
FROM @Plan pl
JOIN @Subjects s ON s.SubIdx = pl.SubIdx;

UPDATE pl SET EsaMarks = pl.Total - pl.TwMarks FROM @Plan pl;

-- Push any overflow back onto TW so neither head can exceed its own out-of.
UPDATE pl
SET TwMarks  = pl.TwMarks + (pl.EsaMarks - s.EsaOutOf),
    EsaMarks = s.EsaOutOf
FROM @Plan pl
JOIN @Subjects s ON s.SubIdx = pl.SubIdx
WHERE pl.EsaMarks > s.EsaOutOf;

UPDATE pl SET EsaMarks = 0, TwMarks = 0 FROM @Plan pl WHERE pl.IsAbsent = 1;

-- MarksMaster: one per student per exam. SGPI/CGPI/ResultRemark/OverallRemark/Rank are left
-- for processing to fill. QuotaType stays NULL -- there is no quota rule in the pharmacy
-- rule set. HMCheck=0 marks this as combined rather than head-of-passing.
DECLARE @MarksMap TABLE (Idx INT PRIMARY KEY, MarksId UNIQUEIDENTIFIER);

INSERT INTO MarksMaster (MarksId, StudentID, StdMstId, ExamId, AcademicYearAYID, SeatNo,
                         SemesterId, Pattern, QuotaType, HMCheck, CollegeId,
                         CreatedAt, IsDeleted)
SELECT NEWID(), r.StudentId, r.StdMstId, @ExamId, @AyId, r.StudentId,
       @SemId, @PatternName, NULL, 0, @CollegeId, @Now, 0
FROM @Roster r
WHERE NOT EXISTS (SELECT 1 FROM MarksMaster mm
                  WHERE mm.StdMstId = r.StdMstId AND mm.ExamId = @ExamId AND mm.IsDeleted = 0);

INSERT INTO @MarksMap (Idx, MarksId)
SELECT r.Idx, mm.MarksId
FROM @Roster r
JOIN MarksMaster mm ON mm.StdMstId = r.StdMstId AND mm.ExamId = @ExamId AND mm.IsDeleted = 0;

-- The seed's target state for every head row: Marks = RawMarks = the planned score, no
-- grace, no grade. Absent heads carry NULL marks and IsAbsent=1 (processing rejects the
-- whole batch if a row has neither a mark nor IsAbsent).
DECLARE @HeadPlan TABLE (MarksId UNIQUEIDENTIFIER, SubjectId UNIQUEIDENTIFIER,
                         CreditsId UNIQUEIDENTIFIER, Head NVARCHAR(50),
                         Marks INT NULL, IsAbsent BIT,
                         PRIMARY KEY (MarksId, SubjectId, Head));

INSERT INTO @HeadPlan (MarksId, SubjectId, CreditsId, Head, Marks, IsAbsent)
SELECT mm.MarksId, sm.SubjectId, sm.CreditsId, h.Head,
       CASE WHEN pl.IsAbsent = 1 THEN NULL
            WHEN h.Head = 'H1' THEN pl.EsaMarks
            ELSE pl.TwMarks END,
       pl.IsAbsent
FROM @Plan pl
JOIN @MarksMap mm ON mm.Idx = pl.Idx
JOIN @SubjectMap sm ON sm.SubIdx = pl.SubIdx
JOIN @Heads h ON h.SubIdx = pl.SubIdx;

INSERT INTO StudentMarks (Id, MarksId, Head, Marks, RawMarks, Resolution, Grace, Remark,
                          Grade, GradePoint, RawGradePoint, IsAbsent, IsCarryForward,
                          SubjectId, CreditsId, CreatedAt, IsDeleted)
SELECT NEWID(), hp.MarksId, hp.Head, hp.Marks, hp.Marks, NULL, NULL, NULL,
       NULL, NULL, NULL, hp.IsAbsent, 0, hp.SubjectId, hp.CreditsId, @Now, 0
FROM @HeadPlan hp
WHERE NOT EXISTS (SELECT 1 FROM StudentMarks sm
                  WHERE sm.MarksId = hp.MarksId AND sm.SubjectId = hp.SubjectId
                    AND sm.Head = hp.Head AND sm.IsDeleted = 0);

-- Re-runs reset marks to the planned state, undoing anything a previous processing run or a
-- manual edit wrote.
UPDATE sm
SET Marks = hp.Marks, RawMarks = hp.Marks, IsAbsent = hp.IsAbsent,
    Resolution = NULL, Grace = NULL, Remark = NULL,
    Grade = NULL, GradePoint = NULL, RawGradePoint = NULL, UpdatedAt = @Now
FROM StudentMarks sm
JOIN @HeadPlan hp ON hp.MarksId = sm.MarksId AND hp.SubjectId = sm.SubjectId
                 AND hp.Head = sm.Head
WHERE sm.IsDeleted = 0;

-- ---------------------------------------------------------------------------------------
-- 7. Verification
-- ---------------------------------------------------------------------------------------
SELECT 'Seeded' AS Info, @CollegeCode AS College,
       (SELECT COUNT(*) FROM SubjectMaster WHERE CollegeId = @CollegeId AND IsDeleted = 0) AS Subjects,
       (SELECT COUNT(*) FROM SubjectCredits sc
         JOIN SubjectCreditMaster scm ON scm.CreditsId = sc.CreditsId
        WHERE scm.CollegeId = @CollegeId AND sc.IsDeleted = 0) AS Heads,
       (SELECT COUNT(*) FROM StudentMaster WHERE CollegeId = @CollegeId AND IsDeleted = 0) AS Students,
       (SELECT COUNT(*) FROM StudentEligibility WHERE CollegeId = @CollegeId AND IsDeleted = 0) AS Eligibilities,
       (SELECT COUNT(*) FROM MarksMaster WHERE CollegeId = @CollegeId AND IsDeleted = 0) AS MarksRecords,
       (SELECT COUNT(*) FROM StudentMarks sm
         JOIN MarksMaster mm ON mm.MarksId = sm.MarksId
        WHERE mm.CollegeId = @CollegeId AND sm.IsDeleted = 0) AS HeadRows,
       (SELECT SUM(CONVERT(INT, TotalCredits)) FROM SubjectCreditMaster
        WHERE CollegeId = @CollegeId AND IsDeleted = 0) AS TotalCredits,
       (SELECT SUM(CONVERT(INT, sc.HeadOutOf)) FROM SubjectCredits sc
         JOIN SubjectCreditMaster scm ON scm.CreditsId = sc.CreditsId
        WHERE scm.CollegeId = @CollegeId AND sc.IsDeleted = 0) AS Aggregate;

-- Must be 0: a head key with no matching SubjectCredits row silently scores out-of 0.
SELECT 'Orphaned head rows (must be 0)' AS Check_, COUNT(*) AS Rows_
FROM StudentMarks sm
JOIN MarksMaster mm ON mm.MarksId = sm.MarksId
WHERE mm.CollegeId = @CollegeId AND sm.IsDeleted = 0
  AND NOT EXISTS (SELECT 1 FROM SubjectCredits sc
                  WHERE sc.CreditsId = sm.CreditsId AND sc.Head = sm.Head AND sc.IsDeleted = 0);

-- Must be 0: processing rejects the batch if any head has neither a mark nor IsAbsent.
SELECT 'Incomplete head rows (must be 0)' AS Check_, COUNT(*) AS Rows_
FROM StudentMarks sm
JOIN MarksMaster mm ON mm.MarksId = sm.MarksId
WHERE mm.CollegeId = @CollegeId AND sm.IsDeleted = 0
  AND sm.Marks IS NULL AND sm.IsAbsent = 0;

-- Expected grace population, computed the same way the engine will.
SELECT 'Expected outcome' AS Info,
       SUM(CASE WHEN d.Deficit BETWEEN 1 AND 3 THEN 1 ELSE 0 END) AS SubjectsGraced,
       SUM(CASE WHEN d.Deficit = 0 THEN 1 ELSE 0 END)             AS SubjectsPassedOutright,
       SUM(CASE WHEN d.Deficit > 3 THEN 1 ELSE 0 END)             AS SubjectsFailed,
       COUNT(DISTINCT CASE WHEN d.Deficit BETWEEN 1 AND 3 THEN d.MarksId END) AS StudentsWithGrace
FROM (
    SELECT sm.MarksId,
           Deficit = CASE WHEN SUM(ISNULL(sm.Marks, 0)) >=
                               CEILING(SUM(CONVERT(INT, sc.HeadOutOf)) * @PassPct / 100.0)
                          THEN 0
                          ELSE CEILING(SUM(CONVERT(INT, sc.HeadOutOf)) * @PassPct / 100.0)
                               - SUM(ISNULL(sm.Marks, 0)) END
    FROM StudentMarks sm
    JOIN MarksMaster mm ON mm.MarksId = sm.MarksId
    JOIN SubjectCredits sc ON sc.CreditsId = sm.CreditsId AND sc.Head = sm.Head AND sc.IsDeleted = 0
    WHERE mm.CollegeId = @CollegeId AND sm.IsDeleted = 0
    GROUP BY sm.MarksId, sm.SubjectId
) d;

COMMIT TRANSACTION;
PRINT 'B.Pharm Sem-VI dataset seeded for PHM001. Process results as admin@pharm.edu.';
