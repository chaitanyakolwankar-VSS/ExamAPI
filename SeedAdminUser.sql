-- =======================================================================================
-- SeedAdminUser.sql   (Phase 0 of the multi-tenancy plan)
-- Creates a REAL, database-backed admin login so the hardcoded admin/admin bypass in
-- AuthService.LoginAsync can be deleted in Phase 1 without locking everyone out.
--
-- WHY THIS IS NEEDED:
--   No existing UserMaster row has a BCrypt hash, so BCrypt.Verify throws and every real
--   login returns HTTP 500. admin/admin is currently the only credential that works.
--
--   Login    : admin@engg.edu   (EMAIL is the login identifier, not Username)
--   Password : Admin@123        <-- DEV/TEST ONLY. Change before this DB is used for real.
--
-- Username is only unique WITHIN a college, so it cannot identify a user across colleges;
-- email is globally unique and resolves to exactly one user, whose CollegeId establishes
-- the tenant for the session.
--
-- The hash below was produced with BCrypt.Net-Next 4.0.3 (the exact package and version
-- referenced by ExamAPI.csproj) and verified round-trip against BCrypt.Verify.
-- To rotate the password, regenerate a hash with that library and replace @HashedPassword.
--
-- Idempotent: re-running updates the existing admin row instead of inserting a duplicate.
-- =======================================================================================

BEGIN TRANSACTION;
SET NOCOUNT ON;

DECLARE @CollegeId      UNIQUEIDENTIFIER = '103EBF99-FEB0-43BC-A312-56FE85D3BCC6';
DECLARE @Username       NVARCHAR(50)     = 'admin';
DECLARE @Email          NVARCHAR(255)    = 'admin@engg.edu';
DECLARE @HashedPassword NVARCHAR(255)    = '$2a$11$EsGrGeQEosc5VEWcnFCPXeItwX4V.UCLMP9h8uvM/0rLCGFlfn6fS';
DECLARE @Now            DATETIME2        = GETUTCDATE();

IF NOT EXISTS (SELECT 1 FROM College WHERE CollegeId = @CollegeId AND IsDeleted = 0)
BEGIN
    RAISERROR('Target college not found; refusing to seed an admin with a dangling CollegeId.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END

-- ---------------------------------------------------------------------------------------
-- 1. A clean 'Admin' role. Every existing Admin/Superadmin row in RoleMaster is
--    soft-deleted, and the ~13 live roles are all junk test names, so we add our own
--    rather than adopt one. AuthService puts Role.Name into the JWT 'role' claim.
--    (RoleMaster gains CollegeId in Phase 2a; it is intentionally global for now.)
-- ---------------------------------------------------------------------------------------
DECLARE @RoleId UNIQUEIDENTIFIER;

SELECT TOP 1 @RoleId = RoleId FROM RoleMaster WHERE Name = 'Admin' AND IsDeleted = 0;

IF @RoleId IS NULL
BEGIN
    SET @RoleId = NEWID();
    INSERT INTO RoleMaster (RoleId, Name, CreatedAt, IsDeleted)
    VALUES (@RoleId, 'Admin', @Now, 0);
END

-- ---------------------------------------------------------------------------------------
-- 2. The admin user itself.
-- ---------------------------------------------------------------------------------------
-- Matched on Email, since that is the login identifier and the globally unique column.
IF EXISTS (SELECT 1 FROM UserMaster WHERE Email = @Email AND IsDeleted = 0)
BEGIN
    UPDATE UserMaster
    SET HashedPassword = @HashedPassword,
        Username       = @Username,
        RoleId         = @RoleId,
        CollegeId      = @CollegeId,
        IsPlatformAdmin = 0,
        UpdatedAt      = @Now
    WHERE Email = @Email AND IsDeleted = 0;
END
ELSE
BEGIN
    INSERT INTO UserMaster (UserId, Username, HashedPassword, Email, FirstName, LastName,
                            RoleId, CollegeId, IsPlatformAdmin, CreatedAt, IsDeleted)
    VALUES (NEWID(), @Username, @HashedPassword, @Email, 'System', 'Administrator',
            @RoleId, @CollegeId, 0, @Now, 0);
END

-- ---------------------------------------------------------------------------------------
-- Verification output
-- ---------------------------------------------------------------------------------------
SELECT 'Seeded admin' AS Info, u.Username, u.Email, r.Name AS Role, c.Name AS College,
       CASE WHEN u.HashedPassword LIKE '$2%' THEN 'bcrypt ok' ELSE 'BAD HASH' END AS PwdState
FROM UserMaster u
LEFT JOIN RoleMaster r ON r.RoleId = u.RoleId
LEFT JOIN College    c ON c.CollegeId = u.CollegeId
WHERE u.Email = @Email AND u.IsDeleted = 0;

-- Other accounts still hold non-bcrypt values; they cannot log in until re-hashed.
SELECT 'Users still without a bcrypt password' AS Info, COUNT(*) AS Cnt
FROM UserMaster WHERE IsDeleted = 0 AND HashedPassword NOT LIKE '$2%';

COMMIT TRANSACTION;
PRINT 'Admin user seeded. Login: admin@engg.edu / Admin@123';
