-- =======================================================================================
-- ProvisionPharmacyCollege.sql   (Phase 2d, expressed in SQL)
--
-- Provisions a SECOND, fully independent college. This is the exact sequence the
-- ProvisionCollege(...) service performs, written out here so the two-college demo can be
-- stood up now and so the C# port has a verified reference.
--
-- A college is not usable until ALL of the following exist -- each one is a hard
-- dependency proven by the code:
--   College                      root
--   AcademicYear (IsCurrent=1)   client reads the AY from its header; nothing loads without it
--   CourseMaster                 exams and subjects hang off a branch
--   PatternMaster                marks/eligibility carry the pattern NAME
--   GradeMaster + GradeThreshold ResultService.GetGradePointFromPercentage THROWS without a
--                                matching threshold
--   RuleSet + Rule(+Cond/Action) ProcessResults fails "No active rule set found" without one
--   RoleMaster                   roles are per-college
--   UserMaster                   somebody has to be able to log in
--
-- Grade scale and ordinance rules are CLONED from the existing college: both colleges sit
-- under the same board, so the University of Mumbai ordinances are identical. Cloning
-- (rather than sharing one row) keeps each college free to diverge later.
--
-- Subjects/students/marks are intentionally NOT created -- that dataset is supplied
-- separately and drops straight into this shell.
--
-- Idempotent: keyed on the college code, so re-running will not duplicate.
-- =======================================================================================

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;   -- required: filtered unique indexes exist on UserMaster

BEGIN TRANSACTION;

DECLARE @Now DATETIME2 = GETUTCDATE();

DECLARE @SourceCollegeId UNIQUEIDENTIFIER = '103EBF99-FEB0-43BC-A312-56FE85D3BCC6'; -- Engineering
DECLARE @NewCollegeId    UNIQUEIDENTIFIER;
DECLARE @CollegeCode     NVARCHAR(20)  = 'PHM001';
DECLARE @AdminEmail      NVARCHAR(255) = 'admin@pharm.edu';

-- Same BCrypt hash as the engineering admin -> password Admin@123 (DEV ONLY).
DECLARE @HashedPassword  NVARCHAR(255) = '$2a$11$EsGrGeQEosc5VEWcnFCPXeItwX4V.UCLMP9h8uvM/0rLCGFlfn6fS';

-- ---------------------------------------------------------------------------------------
-- 1. College
-- ---------------------------------------------------------------------------------------
SELECT @NewCollegeId = CollegeId FROM College WHERE CollegeCode = @CollegeCode AND IsDeleted = 0;

IF @NewCollegeId IS NULL
BEGIN
    SET @NewCollegeId = NEWID();
    INSERT INTO College (CollegeId, Name, CollegeCode, CollegeCenter, Address,
                         ContactEmail, ContactPhone, CreatedAt, IsDeleted)
    VALUES (@NewCollegeId, 'Pharmacy College', @CollegeCode, 'Mumbai', '',
            'contact@pharm.edu', '9000000002', @Now, 0);
END

-- ---------------------------------------------------------------------------------------
-- 2. Academic year -- mirrors the source college's current year so both demo side by side.
-- ---------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM AcademicYear WHERE CollegeId = @NewCollegeId AND IsDeleted = 0)
BEGIN
    INSERT INTO AcademicYear (AYID, FullDuration, ShortDuration, IsCurrent, CollegeId, CreatedAt, IsDeleted)
    SELECT NEWID(), FullDuration, ShortDuration, IsCurrent, @NewCollegeId, @Now, 0
    FROM AcademicYear WHERE CollegeId = @SourceCollegeId AND IsDeleted = 0;
END

-- ---------------------------------------------------------------------------------------
-- 3. Course / branch
-- ---------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM CourseMaster WHERE CollegeId = @NewCollegeId AND IsDeleted = 0)
BEGIN
    INSERT INTO CourseMaster (CourseId, Name, CourseCode, CollegeId, CreatedAt, IsDeleted)
    VALUES (NEWID(), 'Bachelor of Pharmacy', 'BPHARM', @NewCollegeId, @Now, 0);
END

-- ---------------------------------------------------------------------------------------
-- 4. Pattern -- same NEP name; the two rows are distinct records owned by each college.
-- ---------------------------------------------------------------------------------------
DECLARE @NewPatternId UNIQUEIDENTIFIER;
SELECT @NewPatternId = PatternId FROM PatternMaster
WHERE CollegeId = @NewCollegeId AND PatternName = 'NEP' AND IsDeleted = 0;

IF @NewPatternId IS NULL
BEGIN
    SET @NewPatternId = NEWID();
    INSERT INTO PatternMaster (PatternId, PatternName, Description, CollegeId, CreatedAt, IsDeleted)
    VALUES (@NewPatternId, 'NEP', 'New education policy', @NewCollegeId, @Now, 0);
END

-- ---------------------------------------------------------------------------------------
-- 5. Grade scale -- cloned WITH its thresholds (GradeThreshold is a child table with no
--    CollegeId of its own; it inherits isolation through its GradeMaster).
-- ---------------------------------------------------------------------------------------
DECLARE @NewGradeMasterId UNIQUEIDENTIFIER;
SELECT @NewGradeMasterId = GradeMasterId FROM GradeMaster
WHERE CollegeId = @NewCollegeId AND IsDeleted = 0;

IF @NewGradeMasterId IS NULL
BEGIN
    DECLARE @SrcGradeMasterId UNIQUEIDENTIFIER =
        (SELECT TOP 1 GradeMasterId FROM GradeMaster WHERE CollegeId = @SourceCollegeId AND IsDeleted = 0);

    SET @NewGradeMasterId = NEWID();

    INSERT INTO GradeMaster (GradeMasterId, Name, Description, CollegeId, CreatedAt, IsDeleted)
    SELECT @NewGradeMasterId, Name, Description, @NewCollegeId, @Now, 0
    FROM GradeMaster WHERE GradeMasterId = @SrcGradeMasterId;

    INSERT INTO GradeThreshold (ThresholdId, Grade, GradePoint, MinPercentage, MaxPercentage,
                                PerformanceRemark, GradeMasterId, CreatedAt, IsDeleted)
    SELECT NEWID(), Grade, GradePoint, MinPercentage, MaxPercentage,
           PerformanceRemark, @NewGradeMasterId, @Now, 0
    FROM GradeThreshold WHERE GradeMasterId = @SrcGradeMasterId AND IsDeleted = 0;
END

-- ---------------------------------------------------------------------------------------
-- 6. Ordinance rule set -- cloned with its rules, conditions and actions.
--    Same board, so the O.5042-A / O.5045-A / O.229 set applies unchanged.
-- ---------------------------------------------------------------------------------------
DECLARE @NewRuleSetId UNIQUEIDENTIFIER;
SELECT @NewRuleSetId = RuleSetId FROM RuleSet WHERE CollegeId = @NewCollegeId AND IsDeleted = 0;

IF @NewRuleSetId IS NULL
BEGIN
    -- Pick the source rule set that actually CARRIES rules. The engineering college also
    -- has an empty leftover CScheme rule set, and an unordered TOP 1 can select that,
    -- silently producing a college whose result processing does nothing.
    DECLARE @SrcRuleSetId UNIQUEIDENTIFIER =
        (SELECT TOP 1 rs.RuleSetId
         FROM RuleSet rs
         WHERE rs.CollegeId = @SourceCollegeId AND rs.IsDeleted = 0
         ORDER BY (SELECT COUNT(*) FROM [Rule] r
                   WHERE r.RuleSetId = rs.RuleSetId AND r.IsDeleted = 0) DESC,
                  rs.CreatedAt);

    IF @SrcRuleSetId IS NULL
       OR NOT EXISTS (SELECT 1 FROM [Rule] WHERE RuleSetId = @SrcRuleSetId AND IsDeleted = 0)
    BEGIN
        RAISERROR('Source college has no rule set with rules; nothing to clone.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    SET @NewRuleSetId = NEWID();

    INSERT INTO RuleSet (RuleSetId, Name, ExamType, IsActive, PatternId, GradeMasterId,
                         CollegeId, CreatedAt, IsDeleted)
    SELECT @NewRuleSetId, Name, ExamType, IsActive, @NewPatternId, @NewGradeMasterId,
           @NewCollegeId, @Now, 0
    FROM RuleSet WHERE RuleSetId = @SrcRuleSetId;

    -- Map each source rule to its clone so conditions/actions can be re-pointed.
    DECLARE @RuleMap TABLE (OldRuleId UNIQUEIDENTIFIER, NewRuleId UNIQUEIDENTIFIER);

    INSERT INTO @RuleMap (OldRuleId, NewRuleId)
    SELECT RuleId, NEWID() FROM [Rule] WHERE RuleSetId = @SrcRuleSetId AND IsDeleted = 0;

    INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, OrdinanceSymbol,
                        RuleSetId, CreatedAt, IsDeleted)
    SELECT m.NewRuleId, r.Name, r.Priority, r.IsEnabled, r.StopOnSuccess, r.OrdinanceSymbol,
           @NewRuleSetId, @Now, 0
    FROM [Rule] r JOIN @RuleMap m ON m.OldRuleId = r.RuleId;

    INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted)
    SELECT NEWID(), c.FactName, c.Operator, c.Value, m.NewRuleId, @Now, 0
    FROM RuleCondition c JOIN @RuleMap m ON m.OldRuleId = c.RuleId
    WHERE c.IsDeleted = 0;

    INSERT INTO RuleAction (ActionId, ActionType, CalculationMode, Param1Type, Param1Value,
                            Param2Type, Param2Value, MaxLimit, MaxTargetCount, Target,
                            Expression, RuleId, CreatedAt, IsDeleted)
    SELECT NEWID(), a.ActionType, a.CalculationMode, a.Param1Type, a.Param1Value,
           a.Param2Type, a.Param2Value, a.MaxLimit, a.MaxTargetCount, a.Target,
           a.Expression, m.NewRuleId, @Now, 0
    FROM RuleAction a JOIN @RuleMap m ON m.OldRuleId = a.RuleId
    WHERE a.IsDeleted = 0;
END

-- ---------------------------------------------------------------------------------------
-- 7. Admin role for this college (roles are per-college).
-- ---------------------------------------------------------------------------------------
DECLARE @NewRoleId UNIQUEIDENTIFIER;
SELECT @NewRoleId = RoleId FROM RoleMaster
WHERE CollegeId = @NewCollegeId AND Name = 'Admin' AND IsDeleted = 0;

IF @NewRoleId IS NULL
BEGIN
    SET @NewRoleId = NEWID();
    INSERT INTO RoleMaster (RoleId, Name, CollegeId, CreatedAt, IsDeleted)
    VALUES (@NewRoleId, 'Admin', @NewCollegeId, @Now, 0);
END

-- ---------------------------------------------------------------------------------------
-- 8. First admin. Username 'admin' duplicates the engineering college's username -- which
--    is exactly what the composite (CollegeId, Username) index now permits, and what the
--    old global unique index made impossible. Email stays globally unique.
-- ---------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM UserMaster WHERE Email = @AdminEmail AND IsDeleted = 0)
BEGIN
    INSERT INTO UserMaster (UserId, Username, HashedPassword, Email, FirstName, LastName,
                            RoleId, CollegeId, IsPlatformAdmin, CreatedAt, IsDeleted)
    VALUES (NEWID(), 'admin', @HashedPassword, @AdminEmail, 'Pharmacy', 'Administrator',
            @NewRoleId, @NewCollegeId, 0, @Now, 0);
END

-- ---------------------------------------------------------------------------------------
-- Verification
-- ---------------------------------------------------------------------------------------
SELECT 'Provisioned' AS Info, c.Name, c.CollegeCode,
       (SELECT COUNT(*) FROM AcademicYear  WHERE CollegeId = c.CollegeId AND IsDeleted = 0) AS AYs,
       (SELECT COUNT(*) FROM CourseMaster  WHERE CollegeId = c.CollegeId AND IsDeleted = 0) AS Courses,
       (SELECT COUNT(*) FROM PatternMaster WHERE CollegeId = c.CollegeId AND IsDeleted = 0) AS Patterns,
       (SELECT COUNT(*) FROM GradeMaster   WHERE CollegeId = c.CollegeId AND IsDeleted = 0) AS GradeScales,
       (SELECT COUNT(*) FROM RuleSet       WHERE CollegeId = c.CollegeId AND IsDeleted = 0) AS RuleSets,
       (SELECT COUNT(*) FROM [Rule] r JOIN RuleSet rs ON rs.RuleSetId = r.RuleSetId
         WHERE rs.CollegeId = c.CollegeId AND r.IsDeleted = 0) AS Rules,
       (SELECT COUNT(*) FROM RoleMaster    WHERE CollegeId = c.CollegeId AND IsDeleted = 0) AS Roles,
       (SELECT COUNT(*) FROM UserMaster    WHERE CollegeId = c.CollegeId AND IsDeleted = 0) AS Users
FROM College c WHERE c.CollegeId = @NewCollegeId;

COMMIT TRANSACTION;
PRINT 'Pharmacy college provisioned. Login: admin@pharm.edu / Admin@123';
