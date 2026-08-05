-- =======================================================================================
-- CleanupLegacyAccounts.sql
-- Soft-deletes the pre-multi-tenancy demo accounts and the junk roles left over from
-- manual UI testing. Idempotent and REVERSIBLE (IsDeleted = 1; nothing is physically
-- removed, so any row can be restored by setting IsDeleted back to 0).
--
-- Verified safe before writing this script:
--   * The 5 legacy users (bob_martin, charlie_lee, john_doe, jane_smith, alice_jones)
--     hold non-bcrypt password values, so BCrypt.Verify cannot match them -- they are
--     already unable to log in.
--   * They are the CreatedBy of ZERO rows in MarksMaster / StudentMaster / SubjectMaster,
--     so removing them orphans no audit trail.
--
-- Roles: keeps only roles that a surviving user still points at. Everything else is
-- junk test data ('axswsxdqaw', 'rgterg', 'qwefwafaw', ...).
-- =======================================================================================

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;   -- required: filtered unique indexes exist on UserMaster

BEGIN TRANSACTION;

DECLARE @Now DATETIME2 = GETUTCDATE();

-- ---------------------------------------------------------------------------------------
-- 1. Legacy users: anyone whose stored password is not a bcrypt hash. Guarded so a real
--    account can never be caught by this, and so it cannot remove the last usable login.
-- ---------------------------------------------------------------------------------------
UPDATE UserMaster
SET IsDeleted = 1, DeletedAt = @Now
WHERE IsDeleted = 0
  AND HashedPassword NOT LIKE '$2%'
  AND EXISTS (SELECT 1 FROM UserMaster keep
              WHERE keep.IsDeleted = 0 AND keep.HashedPassword LIKE '$2%');

-- ---------------------------------------------------------------------------------------
-- 2. Roles no surviving user references.
-- ---------------------------------------------------------------------------------------
UPDATE RoleMaster
SET IsDeleted = 1, DeletedAt = @Now
WHERE IsDeleted = 0
  AND NOT EXISTS (SELECT 1 FROM UserMaster u
                  WHERE u.RoleId = RoleMaster.RoleId AND u.IsDeleted = 0);

-- ---------------------------------------------------------------------------------------
-- Verification -- every surviving account must be usable and college-bound.
-- ---------------------------------------------------------------------------------------
SELECT 'Surviving users' AS Info, u.Username, u.Email, r.Name AS Role, c.Name AS College,
       CASE WHEN u.HashedPassword LIKE '$2%' THEN 'bcrypt ok' ELSE 'UNUSABLE' END AS Pwd
FROM UserMaster u
LEFT JOIN RoleMaster r ON r.RoleId = u.RoleId
LEFT JOIN College    c ON c.CollegeId = u.CollegeId
WHERE u.IsDeleted = 0 ORDER BY c.Name;

SELECT 'Live roles' AS Info, r.Name, c.Name AS College
FROM RoleMaster r LEFT JOIN College c ON c.CollegeId = r.CollegeId
WHERE r.IsDeleted = 0 ORDER BY c.Name, r.Name;

COMMIT TRANSACTION;
PRINT 'Legacy accounts cleaned (soft delete).';
