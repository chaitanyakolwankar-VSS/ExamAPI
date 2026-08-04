-- =======================================================================================
-- NormalizeRuleOperators.sql
-- Rewrites RuleCondition.Operator from the word form ('Equals', 'GreaterOrEqual') to the
-- symbol form ('==', '>=') for the engineering college's rule sets.
--
-- WHY:
--   ResultService.CompareValues accepts BOTH forms in the same switch arm
--   ("Equals" or "==" => ...), so both work at runtime and this change is behaviour-neutral.
--   The Ordinance editor is the problem: its operator dropdown is populated from
--   OrdinanceService.GetEngineMetadataAsync, which offers ONLY the symbol form. A <select>
--   whose value matches no option displays the FIRST option ('=='), so a rule stored as
--   'GreaterOrEqual' rendered as '==' and saving it silently rewrote '>= 1' into '== 1'.
--   The client now maps word -> symbol on load, but normalizing the stored data removes the
--   dual representation entirely rather than relying on that alias map surviving.
--
-- CAUTION: CompareValues ends in `_ => false`. An operator it does not recognise does not
--   throw -- the condition evaluates false forever and the rule silently stops firing. The
--   whitelist + post-check below exist so a bad value can never be committed.
--
-- Scope: engineering college only. The pharmacy college (PHM001) already stores symbols.
--   Soft-deleted conditions are left alone.
--
-- Idempotent: re-running finds nothing to map and reports 0. Safe to re-run.
-- Reverse: swap WordForm/Symbol in @Map (both forms are equally valid to the engine).
-- =======================================================================================

SET NOCOUNT ON;

BEGIN TRANSACTION;

DECLARE @Now DATETIME2 = GETUTCDATE();
DECLARE @CollegeId UNIQUEIDENTIFIER = '103EBF99-FEB0-43BC-A312-56FE85D3BCC6'; -- Engineering

DECLARE @Map TABLE (WordForm NVARCHAR(20) PRIMARY KEY, Symbol NVARCHAR(20));
INSERT INTO @Map (WordForm, Symbol) VALUES
 ('Equals',             '=='),
 ('NotEquals',          '!='),
 ('GreaterThan',        '>'),
 ('LessThan',           '<'),
 ('GreaterOrEqual',     '>='),
 ('GreaterThanOrEqual', '>='),
 ('LessOrEqual',        '<='),
 ('LessThanOrEqual',    '<=');

DECLARE @Pending INT = (
    SELECT COUNT(*)
    FROM RuleCondition c
    JOIN [Rule] r  ON r.RuleId    = c.RuleId
    JOIN RuleSet s ON s.RuleSetId = r.RuleSetId
    WHERE s.CollegeId = @CollegeId AND c.IsDeleted = 0
      AND c.Operator IN (SELECT WordForm FROM @Map));

UPDATE c
SET c.Operator = m.Symbol,
    c.UpdatedAt = @Now
FROM RuleCondition c
JOIN [Rule] r  ON r.RuleId    = c.RuleId
JOIN RuleSet s ON s.RuleSetId = r.RuleSetId
JOIN @Map m    ON m.WordForm  = c.Operator
WHERE s.CollegeId = @CollegeId AND c.IsDeleted = 0;

-- Nothing may be left that CompareValues would not recognise.
IF EXISTS (
    SELECT 1
    FROM RuleCondition c
    JOIN [Rule] r  ON r.RuleId    = c.RuleId
    JOIN RuleSet s ON s.RuleSetId = r.RuleSetId
    WHERE s.CollegeId = @CollegeId AND c.IsDeleted = 0
      AND c.Operator NOT IN ('==', '!=', '>', '>=', '<', '<='))
BEGIN
    RAISERROR('Unrecognised operator remains after mapping; rolling back.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END

SELECT 'Normalized' AS Info, @Pending AS RowsMapped;

SELECT 'Operators now in use' AS Info, c.Operator, COUNT(*) AS Conditions
FROM RuleCondition c
JOIN [Rule] r  ON r.RuleId    = c.RuleId
JOIN RuleSet s ON s.RuleSetId = r.RuleSetId
WHERE s.CollegeId = @CollegeId AND c.IsDeleted = 0
GROUP BY c.Operator
ORDER BY c.Operator;

COMMIT TRANSACTION;
PRINT 'Rule operators normalized to symbol form for the engineering college.';
