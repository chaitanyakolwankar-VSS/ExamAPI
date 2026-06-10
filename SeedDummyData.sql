-- =======================================================================================
-- SeedDummyData.sql
-- Use this script to insert dummy data for testing Marks Entry and Overall Marks Entry.
-- It populates: College, Course, Pattern, Academic Year, Exam, Subjects, Credits, Rules,
-- and 10 Students with Marks Master & Student Marks records.
-- =======================================================================================

BEGIN TRANSACTION;

-- 1. Create Core Master Data
DECLARE @CollegeId UNIQUEIDENTIFIER = '103EBF99-FEB0-43BC-A312-56FE85D3BCC6'; -- The dummy ID used in JWT logic
IF NOT EXISTS(SELECT 1 FROM Colleges WHERE CollegeId = @CollegeId)
BEGIN
    INSERT INTO Colleges (CollegeId, Name, CollegeCode, CollegeCenter, ContactEmail, ContactPhone, CreatedAt, IsDeleted)
    VALUES (@CollegeId, 'Dummy Engineering College', 'DEC-01', 'Center-1', 'admin@dec.edu', '1234567890', GETUTCDATE(), 0);
END

DECLARE @CourseId UNIQUEIDENTIFIER = NEWID();
INSERT INTO CourseMasters (CourseId, Name, CourseCode, CollegeId, CreatedAt, IsDeleted)
VALUES (@CourseId, 'B.Tech Computer Science', 'CS-01', @CollegeId, GETUTCDATE(), 0);

DECLARE @AyId UNIQUEIDENTIFIER = NEWID();
INSERT INTO AcademicYears (AYID, FullDuration, ShortDuration, IsCurrent, CollegeId, CreatedAt, IsDeleted)
VALUES (@AyId, '2025-2026', '25-26', 1, @CollegeId, GETUTCDATE(), 0);

DECLARE @PatternId UNIQUEIDENTIFIER = NEWID();
DECLARE @PatternName NVARCHAR(100) = 'CBCGS-2025';
INSERT INTO PatternMasters (PatternId, PatternName, Description, CollegeId, CreatedAt, IsDeleted)
VALUES (@PatternId, @PatternName, 'Choice Based Grading System', @CollegeId, GETUTCDATE(), 0);

DECLARE @ExamId UNIQUEIDENTIFIER = NEWID();
INSERT INTO Exams (ExamId, Name, ExamType, Semester, CourseId, PatternId, AcademicYearId, CreatedAt, IsDeleted)
VALUES (@ExamId, 'Sem-1 Regular Exam', 'Regular', 'Sem-1', @CourseId, @PatternId, @AyId, GETUTCDATE(), 0);


-- 2. Create Subjects and Credits
-- Subject 1 (Theory + TW)
DECLARE @Sub1Id UNIQUEIDENTIFIER = NEWID();
INSERT INTO SubjectMasters (SubjectId, SubjectName, SubjectCode, Semester, CourseId, CreatedAt, IsDeleted)
VALUES (@Sub1Id, 'Engineering Mathematics I', 'MATH101', 'Sem-1', @CourseId, GETUTCDATE(), 0);

DECLARE @Sub1CredMstId UNIQUEIDENTIFIER = NEWID();
INSERT INTO SubjectCreditMasters (CreditsId, TotalCredits, AYID, SubjectId, CreatedAt, IsDeleted)
VALUES (@Sub1CredMstId, '5', '2025-2026', @Sub1Id, GETUTCDATE(), 0);

DECLARE @Sub1Cred1Id UNIQUEIDENTIFIER = NEWID();
INSERT INTO SubjectCredits (Id, Head, HeadCredit, HeadOutOf, HeadPass, CreditsId, CreatedAt, IsDeleted)
VALUES (@Sub1Cred1Id, 'Theory', '4', '60', '24', @Sub1CredMstId, GETUTCDATE(), 0);
DECLARE @Sub1Cred2Id UNIQUEIDENTIFIER = NEWID();
INSERT INTO SubjectCredits (Id, Head, HeadCredit, HeadOutOf, HeadPass, CreditsId, CreatedAt, IsDeleted)
VALUES (@Sub1Cred2Id, 'TermWork', '1', '40', '16', @Sub1CredMstId, GETUTCDATE(), 0);

-- Subject 2 (Theory only)
DECLARE @Sub2Id UNIQUEIDENTIFIER = NEWID();
INSERT INTO SubjectMasters (SubjectId, SubjectName, SubjectCode, Semester, CourseId, CreatedAt, IsDeleted)
VALUES (@Sub2Id, 'Engineering Physics', 'PHY101', 'Sem-1', @CourseId, GETUTCDATE(), 0);

DECLARE @Sub2CredMstId UNIQUEIDENTIFIER = NEWID();
INSERT INTO SubjectCreditMasters (CreditsId, TotalCredits, AYID, SubjectId, CreatedAt, IsDeleted)
VALUES (@Sub2CredMstId, '4', '2025-2026', @Sub2Id, GETUTCDATE(), 0);

DECLARE @Sub2Cred1Id UNIQUEIDENTIFIER = NEWID();
INSERT INTO SubjectCredits (Id, Head, HeadCredit, HeadOutOf, HeadPass, CreditsId, CreatedAt, IsDeleted)
VALUES (@Sub2Cred1Id, 'Theory', '4', '60', '24', @Sub2CredMstId, GETUTCDATE(), 0);


-- 3. Ordinance Engine Rules (Grace Marks for Theory)
DECLARE @RuleSetId UNIQUEIDENTIFIER = NEWID();
INSERT INTO RuleSets (RuleSetId, PatternId, IsActive, CreatedAt, IsDeleted)
VALUES (@RuleSetId, @PatternId, 1, GETUTCDATE(), 0);

-- Rule 1: Allow up to 3 marks grace in Theory if overall failed subjects is exactly 1
DECLARE @Rule1Id UNIQUEIDENTIFIER = NEWID();
INSERT INTO Rules (RuleId, Name, Priority, IsEnabled, StopOnSuccess, OrdinanceSymbol, RuleSetId, CreatedAt, IsDeleted)
VALUES (@Rule1Id, '1 Subject Grace (Max 3)', 1, 1, 1, '*', @RuleSetId, GETUTCDATE(), 0);

INSERT INTO RuleConditions (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted)
VALUES (NEWID(), 'FailedSubjectCount', 'Equals', '1', @Rule1Id, GETUTCDATE(), 0);

INSERT INTO RuleActions (ActionId, ActionType, MaxLimit, Param1Type, Param1Value, RuleId, CreatedAt, IsDeleted)
VALUES (NEWID(), 'AddGrace', 3, 'Absolute', 3, @Rule1Id, GETUTCDATE(), 0);

-- Rule 2: NSS Bonus SGPI Rule
DECLARE @Rule2Id UNIQUEIDENTIFIER = NEWID();
INSERT INTO Rules (RuleId, Name, Priority, IsEnabled, StopOnSuccess, RuleSetId, CreatedAt, IsDeleted)
VALUES (@Rule2Id, 'NSS SGPI Bonus', 2, 1, 0, @RuleSetId, GETUTCDATE(), 0);

INSERT INTO RuleConditions (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted)
VALUES (NEWID(), 'HasQuota', 'Equals', '1', @Rule2Id, GETUTCDATE(), 0);

INSERT INTO RuleActions (ActionId, ActionType, Param1Value, RuleId, CreatedAt, IsDeleted)
VALUES (NEWID(), 'AddBonusSGPI', 0.1, @Rule2Id, GETUTCDATE(), 0);


-- 4. Create Students and Marks Entry
-- We will create 10 students. 
DECLARE @i INT = 1;
DECLARE @StdMstId UNIQUEIDENTIFIER;
DECLARE @MarksMstId UNIQUEIDENTIFIER;
DECLARE @Quota NVARCHAR(20);

WHILE @i <= 10
BEGIN
    SET @StdMstId = NEWID();
    SET @MarksMstId = NEWID();
    
    -- Every 3rd student is NSS
    SET @Quota = CASE WHEN @i % 3 = 0 THEN 'NSS' ELSE NULL END;

    INSERT INTO StudentMasters (StdMstId, StudentId, FirstName, LastName, CourseId, CollegeId, CreatedAt, IsDeleted)
    VALUES (@StdMstId, 'STD2025' + RIGHT('000' + CAST(@i AS VARCHAR(3)), 3), 'Student' + CAST(@i AS VARCHAR(2)), 'Test', @CourseId, @CollegeId, GETUTCDATE(), 0);

    INSERT INTO MarksMasters (MarksId, StudentID, StdMstId, ExamId, AcademicYearAYID, SeatNo, SemesterId, Pattern, QuotaType, HMCheck, Rank, OverallRemark, CreatedAt, IsDeleted)
    VALUES (@MarksMstId, 'STD2025' + RIGHT('000' + CAST(@i AS VARCHAR(3)), 3), @StdMstId, @ExamId, @AyId, 'S10' + CAST(@i AS VARCHAR(2)), 'Sem-1', @PatternName, @Quota, 1, @i, 'Pending', GETUTCDATE(), 0);

    -- Insert Head Marks
    -- For variety: 
    -- Student 1: Passes all beautifully (60/60)
    -- Student 2: Fails 1 subject closely (22/60) -> should get graced to 24 by Rule 1
    -- Student 3: Fails miserably (10/60) -> no grace will help
    -- Student 4: Absent ('Ab')
    -- Students 5-10: Random marks

    DECLARE @Sub1ThMarks NVARCHAR(10) = CAST(25 + (@i * 3) AS NVARCHAR);
    DECLARE @Sub1TwMarks NVARCHAR(10) = CAST(20 + (@i * 2) AS NVARCHAR);
    DECLARE @Sub2ThMarks NVARCHAR(10) = CAST(24 + (@i * 2) AS NVARCHAR);

    IF @i = 1 
    BEGIN
        SET @Sub1ThMarks = '60'; SET @Sub1TwMarks = '40'; SET @Sub2ThMarks = '60';
    END
    ELSE IF @i = 2
    BEGIN
        -- Close fail (needs 2 grace marks)
        SET @Sub1ThMarks = '60'; SET @Sub1TwMarks = '40'; SET @Sub2ThMarks = '22';
    END
    ELSE IF @i = 3
    BEGIN
        -- Miserable fail
        SET @Sub1ThMarks = '10'; SET @Sub1TwMarks = '10'; SET @Sub2ThMarks = '10';
    END
    ELSE IF @i = 4
    BEGIN
        -- Absent
        SET @Sub1ThMarks = NULL; SET @Sub1TwMarks = NULL; SET @Sub2ThMarks = NULL;
    END

    -- Insert Subject 1 (Theory)
    INSERT INTO StudentMarks (Id, MarksId, Head, Marks, Remark, SubjectId, CreditsId, CreatedAt, IsDeleted)
    VALUES (NEWID(), @MarksMstId, 'Theory', CASE WHEN @i = 4 THEN NULL ELSE CAST(@Sub1ThMarks AS INT) END, CASE WHEN @i = 4 THEN 'Ab' ELSE 'Successful' END, @Sub1Id, @Sub1CredMstId, GETUTCDATE(), 0);

    -- Insert Subject 1 (TW)
    INSERT INTO StudentMarks (Id, MarksId, Head, Marks, Remark, SubjectId, CreditsId, CreatedAt, IsDeleted)
    VALUES (NEWID(), @MarksMstId, 'TermWork', CASE WHEN @i = 4 THEN NULL ELSE CAST(@Sub1TwMarks AS INT) END, CASE WHEN @i = 4 THEN 'Ab' ELSE 'Successful' END, @Sub1Id, @Sub1CredMstId, GETUTCDATE(), 0);

    -- Insert Subject 2 (Theory)
    INSERT INTO StudentMarks (Id, MarksId, Head, Marks, Remark, SubjectId, CreditsId, CreatedAt, IsDeleted)
    VALUES (NEWID(), @MarksMstId, 'Theory', CASE WHEN @i = 4 THEN NULL ELSE CAST(@Sub2ThMarks AS INT) END, CASE WHEN @i = 4 THEN 'Ab' ELSE 'Successful' END, @Sub2Id, @Sub2CredMstId, GETUTCDATE(), 0);

    SET @i = @i + 1;
END

COMMIT TRANSACTION;
PRINT 'Dummy Data Successfully Seeded!';
