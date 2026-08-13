# ATKT / Revaluation exam assignment — migration snapshot

Migration of the legacy WebForm `OldProjects/60_40_LTCE/frm_atktreval_exm_assign.aspx`
(1869-line code-behind) onto ExamAPI + ExamClient.

**Updated:** 2026-08-13, backend complete.
**Branch:** `feature/combined-passing` (both ExamAPI and ExamClient).

---

## 1. Where it stands

| | Status |
|---|---|
| Backend | **Complete.** Builds with 0 errors (271 warnings, all pre-existing nullability/unused-variable noise — unchanged from before this work). 45 new tests, all passing. |
| Frontend | Written but **never type-checked, never wired, never run**. |
| Schema | **No change.** No new tables, no migration. |
| Ordinance / marks / result / gazette chain | One extracted method, covered by the existing green tests. |

```
dotnet test ExamAPI/ExamAPI.Tests/ExamAPI.Tests.csproj
Failed: 1, Passed: 68, Total: 69
```

Baseline before this work was 23 passed / 1 failed. The same single failure remains and is
**pre-existing and unrelated** — `SeedExcelDataTest.BulkSeedFromExcel` is not a unit test, it
connects to the live `DBExamAPI` and dies cleaning up `StudentsOverallResult` rows:

```
The DELETE statement conflicted with the REFERENCE constraint
"FK_StudentsOverallResult_StudentMaster_StdMstId"
```

---

## 2. How it works

### The assignment record

A student's registration into a follow-up exam is a `MarksMaster` for the target exam plus one
`StudentMarks` row per head. No new table. The legacy string encodings map onto columns that
already existed:

| Legacy | New |
|---|---|
| `h1` ends with `'+'` | `StudentMarks.IsCarryForward = true` — not appearing, mark carried over |
| `h1` has no `'+'` | `StudentMarks.IsCarryForward = false` — appearing, fresh attempt |
| `h1` contains `'Ab'` | `StudentMarks.IsAbsent` |
| row exists for `exam_code` | `MarksMaster` for the target `ExamId` |
| `remark = 'UnSuccessful'` | `StudentSubjectResult.SubjectStatus`, or `SubjectPassEvaluator` when results are unprocessed |
| `'R' + exam_code` | `ExamMaster.RevaluationForExamId` |

### Configuration comes from the Ordinance engine

There is no assignment config table. `AtktRevalExamService` resolves a `RuleSet` by
`(request.Pattern, targetExam.ExamType)` — the same lookup `ResultService` uses. Pattern comes
from the screen, not the exam, because `ExamMaster` has no `PatternId` and its `Semester` is
never populated.

Inside that rule set, any rule carrying an **`AllowExamAssignment`** action grants assignment:

| Rule element | Meaning here |
|---|---|
| `RuleCondition` | Eligibility gate, evaluated per student against their source attempt via the normal `IFactProvider` registry |
| `RuleAction.Target` | Which subjects (by status) and which heads are in scope |
| `RuleAction.MaxTargetCount` | Cap on subjects per student, 0 = uncapped |
| `Rule.Priority` / `StopOnSuccess` | Honoured; several granting rules union their scopes |

This replaces the legacy literals: `exam_code LIKE 'EXM%' AND atkt_exam = 1`,
`remark = 'UnSuccessful'`, "a passed subject gets no checkbox", "an absent student cannot be
revalued", and the `"ESE"` head literal.

Safe to park in a KT rule set alongside ordinance rules: `ResultService.cs:207-211` looks up a
handler and does `if (handler != null)`, so an action type with no registered handler is
silently skipped by result processing.

**Revaluation** inherits its parent's `ExamType`, so it cannot be identified by that. The
service detects it from `RevaluationForExamId` and looks for a rule set whose `ExamType` is
`REVAL` or `REVALUATION`.

**Without any rule set** the screen still works on built-in defaults: ATKT offers failed,
absent and never-attempted subjects; revaluation offers subjects that carry a mark; every head
of a selected subject is re-attempted; no cap.

### Target vocabulary — `HeadTargetSpec`

`Services/Result/Engine/HeadTarget.cs`.

- **Subject scope**: `AllSubjects` | `FailingSubjects` | `PassingSubjects` | `AbsentSubjects` |
  `NotAttempted`. Omit all to mean every subject. The keyword set is the union of the two
  vocabularies the existing handlers already accept, so no seeded rule changes meaning.
- **Head names**: anything else, e.g. `ESE`. Matched against `SubjectCredits.HeadType` (the
  printed label) **and** the positional key `StudentMarks.Head` (`H1`/`H2`), normalised to
  uppercase alphanumerics. Omit to re-attempt every head.

### Combined passing

Every verdict routes through `SubjectPassEvaluator`, which branches on each subject's own
`PassingStrategy`. One exam mixes strategies, so the grid shows `40/100` for a combined subject
and `32+8/100` for a head-wise one in the same row. Test coverage proves the same marks
(30/80 + 18/20) pass combined at 40% and fail head-wise.

Only heads inside the granted scope are blanked for a fresh attempt; every other head carries
its marks forward. That makes "re-sit the theory paper, keep the term work" expressible, and it
is load bearing for combined subjects, whose verdict is the sum across heads — a blanked
carry-forward head would drop the total and fail a student on data loss rather than performance.

---

## 3. Files

### Backend — complete

| File | |
|---|---|
| `ExamAPI/Services/AtktRevalExam/AtktRevalExamService.cs` | the screen |
| `ExamAPI/Services/AtktRevalExam/IAtktRevalExamService.cs` | |
| `ExamAPI/Controllers/AtktRevalExamController.cs` | 6 endpoints |
| `ExamAPI/DTOs/AtktRevalExamDto.cs` | contract |
| `ExamAPI/Models/AssignmentModes.cs` | ATKT / Revaluation |
| `ExamAPI/Services/Result/Engine/HeadTarget.cs` | Target parsing, shared |
| `ExamAPI/Services/Result/Engine/RuleConditionEvaluator.cs` | condition evaluation, shared with ResultService |
| `ExamAPI.Tests/AtktRevalExamServiceTests.cs` | 45 tests |
| `SeedAtktRevalRuleSets.sql` | ATKT + Revaluation rule sets for NEP |

Endpoints, all under `api/AtktRevalExam`:
`GET source-exams`, `GET target-exams`, `POST matrix`, `POST save`, `POST assign-all`,
`DELETE assignment`, `POST export`.

### Frontend — written, unverified

| File | |
|---|---|
| `ExamClient/src/pages/Staff/ConductExam/AtktRevalExam.tsx` | 824 lines |
| `ExamClient/src/services/AtktRevalExamService.ts` | 245 lines |

Neither is routed. `App.tsx` has no route; the sidebar's `"ATKT/Reval Exam"` entry still points
at `/blank`. **Both were written against the earlier `AtktPolicyDto` shape and need updating** —
see §5.

---

## 4. Edits to existing, working code

Both covered by the green suite.

1. **`Services/Result/ResultService.cs`** — `CompareValues` delegates to
   `RuleConditionEvaluator.Compare`. The switch expression moved verbatim; both the symbol and
   word spelling of every operator is covered by a table-driven test.

2. **`Services/Exam/ExamService.cs`** — `CreateExamAsync` now assigns
   `exam.ExamId = Guid.NewGuid()` before adding the revaluation twin. Previously
   `RevaluationForExamId = exam.ExamId` was read **before** `SaveChangesAsync`, so the PK was
   still `Guid.Empty` and **every revaluation exam ever created was stored unlinked**.

   > Existing rows are still broken. Any `ExamMaster` with
   > `RevaluationForExamId = '00000000-0000-0000-0000-000000000000'` needs a manual backfill
   > pointing it at its parent exam. Revaluation assignment cannot find its target without this.

---

## 5. Open items

### 5a. `RuleAction.Target` compares the wrong column in the two ordinance handlers

`SeedPharmacyBPharmSem6.sql:298-300` states the contract: `Head` is the positional key,
`HeadType` is the printed label. Every user-facing surface reads the label — gazette
(`ReportService.cs:73`), marks-entry grid (`MarksEntryService.cs:83`), validation messages
(`MarksEntryService.cs:315,323,328`), hall ticket (`GenerateHallTicketService.cs:69`). The
Ordinance UI tells authors to type *"the exact configured head name, such as ESE(TH)"*.

But both Target resolvers compare the positional key:

- `AddGraceHandler.cs:81` — `!headTargets.Contains(sm.Head.ToUpperInvariant())`
- `UpgradeGradeHandler.cs:42` — `targets.Contains(sm.Head.ToUpperInvariant())`

and they use different keyword vocabularies (`IsAllFailingHeadsTarget` vs
`IsAllSubjectsTarget`). An author who follows the UI hint gets a filter matching nothing and an
action that silently no-ops. Undetected because every seeded rule uses the keyword
`'FailingSubjects'` (`SeedPharmacyBPharmSem6.sql:264`), never a real head name.

`HeadTargetSpec` fixes this and is wired into **exam assignment only**. The two handlers are
deliberately untouched.

> **Before switching them over:** query live `RuleAction.Target` values. Any hand-authored rule
> with a real head name is a no-op today and would **start firing**, changing result output.

### 5b. Frontend needs the DTO change applied

`AtktPolicyDto` was reshaped when the policy table was dropped. It no longer carries
`requireFailedSubject`, `offerPassedSubjects`, `blockAbsentStudents`, `eligibleHeadTypes`,
`subjectsPerRow`, etc. It now carries `ruleSetId`, `ruleSetName`, `examType`, `mode`,
`isConfigured`, `subjectScopes`, `headTypes`, `maxSubjectsPerStudent`, `rules`.

Also removed: `AtktMatrixRequest.policyId`, and the `GET policies` endpoint. Added:
`AtktCellDto.deficit`.

### 5c. Revaluation needs an exam type in the Ordinance UI

`Ordinance.tsx:139-143` offers `Regular` / `KT` / `REEXAM`. Add
`{ value: "REVAL", label: "Revaluation" }` so a revaluation rule set can be authored in the UI.
One array entry, no schema change.

---

## 6. Deliberately out of scope

**Group / division scoping.** Legacy `cre_marks_tbl.extra2` holds a group id and scopes the
whole screen (`ddl_group`, `std_ext_qry`). There is no Group concept anywhere in the new schema,
models, services or client — the only trace is an unused `groupId?: string` in
`ReportService.DownloadGazetteParams`. Adding it is a cross-cutting feature, not part of this
form.

**Legacy dead paths.** `ddl_pattern` only ever offered `R-2024`, so `load_grd()`,
`btn_save_Click`, `lnk_delete_Click`, `btn_yes_Click` and the whole GridView pipeline were
unreachable in the live build. Their head-wise threshold and ESE-only-reval rules are captured
above; the code itself is not being ported.

---

## 7. Remaining work

1. Apply the DTO changes to the React service and page (§5b).
2. Add the `REVAL` exam type to the Ordinance UI (§5c).
3. Type-check the frontend and wire it: route in `App.tsx`, sidebar path off `/blank`.
4. Run `SeedAtktRevalRuleSets.sql` and exercise the screen end to end.
5. Backfill `RevaluationForExamId = Guid.Empty` rows (§4).
6. Query live `RuleAction.Target` values, then decide on the handler fix (§5a).
7. Manually re-run marks entry → result → gazette once as a regression check.
