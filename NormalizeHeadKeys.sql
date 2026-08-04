/*
    NormalizeHeadKeys.sql  --  idempotent, safe to re-run.

    Brings legacy seeded rows onto the head convention the application already writes:

      SubjectCredits.Head      positional key   "H1" (exam side) / "H2" (internal side)
      SubjectCredits.HeadType  semantic label   "ESE" / "IA" / "TW" / "PR OR"  (what gets printed)

    Rows created through Subject Master already look like this; only data seeded directly by
    SQL deviates ("Theory"/"TermWork"/"TW"/"PR OR" in Head, "TH"/"TW"/"PR" in HeadType).
    StudentMarks.Head is a denormalised copy and is updated in lockstep.

    Mapping applied (legacy -> new):
      Head 'Theory'   / TH  ->  H1, ESE
      Head 'PR OR'    / PR  ->  H1, PR OR
      Head 'TermWork' / TW  ->  H2, IA
      Head 'TW'       / TW  ->  H2, TW

    Run AFTER the EF migration AddCombinedPassingAndSubjectResults.
*/

SET NOCOUNT ON;
BEGIN TRANSACTION;

-- StudentMarks first: it is matched to SubjectCredits by the OLD head label.
UPDATE sm
SET sm.Head = CASE WHEN sc.Head IN ('Theory', 'PR OR') THEN 'H1' ELSE 'H2' END
FROM StudentMarks sm
INNER JOIN SubjectCredits sc
        ON sc.CreditsId = sm.CreditsId
       AND sc.Head = sm.Head
       AND sc.IsDeleted = 0
WHERE sm.Head NOT IN ('H1', 'H2');

-- ResolutionMaster.Head is already 'H1'/'H2'; only the semantic label needs correcting.
UPDATE SubjectCredits
SET HeadType = CASE Head
                   WHEN 'Theory'   THEN 'ESE'
                   WHEN 'TermWork' THEN 'IA'
                   WHEN 'PR OR'    THEN 'PR OR'
                   WHEN 'TW'       THEN 'TW'
                   ELSE HeadType
               END,
    Head     = CASE WHEN Head IN ('Theory', 'PR OR') THEN 'H1' ELSE 'H2' END
WHERE Head NOT IN ('H1', 'H2')
  AND IsDeleted = 0;

-- Verification: both columns should now be H1/H2 only, and no mark should be orphaned.
SELECT 'SubjectCredits' AS TableName, Head, HeadType, COUNT(*) AS Rows
FROM SubjectCredits WHERE IsDeleted = 0 GROUP BY Head, HeadType;

SELECT 'StudentMarks' AS TableName, Head, COUNT(*) AS Rows
FROM StudentMarks WHERE IsDeleted = 0 GROUP BY Head;

SELECT 'Orphaned marks (must be 0)' AS Check_, COUNT(*) AS Rows
FROM StudentMarks sm
WHERE sm.IsDeleted = 0
  AND NOT EXISTS (SELECT 1 FROM SubjectCredits sc
                  WHERE sc.CreditsId = sm.CreditsId AND sc.Head = sm.Head AND sc.IsDeleted = 0);

COMMIT TRANSACTION;
