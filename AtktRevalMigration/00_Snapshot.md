# ATKT / Revaluation exam assignment — migration snapshot

Migration of the legacy WebForm `OldProjects/60_40_LTCE/frm_atktreval_exm_assign.aspx`
(1869-line code-behind) onto ExamAPI + ExamClient.

**Snapshot taken:** 2026-08-13, before rewiring assignment config onto the Ordinance rule engine.
**Branch:** `feature/combined-passing` (both ExamAPI and ExamClient).

---

## 1. Verified baseline — what is known working right now

`dotnet test ExamAPI/ExamAPI.Tests/ExamAPI.Tests.csproj`

```
Failed:  1, Passed: 23, Skipped: 0, Total: 24
```

Passing: `ResultServiceTests`, `AddGraceHandlerTests`, `MarksEntryServiceTests`,
`SubjectPassEvaluatorTests`, `DbModelDebugTests`.

The single failure is **pre-existing and unrelated**:

```
ExamAPI.Tests.SeedExcelDataTest.BulkSeedFromExcel
System.Exception : Bulk seed failed: The DELETE statement conflicted with the
REFERENCE constraint "FK_StudentsOverallResult_StudentMaster_StdMstId".
The conflict occurred in database "DBExamAPI", table "dbo.StudentsOverallResult".
```

It is not a unit test — it connects to the live `DBExamAPI` and fails cleaning up
`StudentsOverallResult` rows before re-seeding `StudentMaster`. Environment-dependent.

`dotnet build ExamAPI/ExamAPI/ExamAPI.csproj` — **0 errors**, 271 warnings (all pre-existing
nullability/unused-variable noise).

### The chain this snapshot protects

```
Ordinance master  ->  Marks entry  ->  Overall marks entry  ->  Result  ->  Gazette / Marksheet
   (RuleSet/Rule/     (StudentMarks     (aggregate + rank)      (SGPI,      (QuestPDF)
    Condition/Action)  raw input)                                grades)
```

Nothing in this migration changes the shape of that chain. See §4 for the single edit that
touches it.

---

## 2. What was built (backend, on disk, compiling)

| File | Status |
|---|---|
| `ExamAPI/Models/ExamAssignmentPolicy.cs` | **being removed** — see §5 |
| `ExamAPI/Migrations/20260812174934_AddExamAssignmentPolicy.*` | **being removed**, never applied to any database |
| `SeedExamAssignmentPolicies.sql` | **being removed** |
| `ExamAPI/DTOs/AtktRevalExamDto.cs` | keep; `AtktPolicyDto` shrinks |
| `ExamAPI/Services/AtktRevalExam/IAtktRevalExamService.cs` | keep |
| `ExamAPI/Services/AtktRevalExam/AtktRevalExamService.cs` | keep; policy resolution rewires to RuleSet |
| `ExamAPI/Services/Result/Engine/RuleConditionEvaluator.cs` | keep — shared with `ResultService` |
| `ExamAPI/Controllers/AtktRevalExamController.cs` | keep |
| `ExamAPI/Services/Exam/ExamService.cs` | bug fix, keep — see §4 |

### The core modelling decision (unchanged by the rewire)

The legacy screen encoded "is this student appearing for this head?" as a trailing `'+'`
inside the `cre_marks_tbl.h1` / `.h2` marks strings. Absent was `'Ab'` in the same string.

That maps onto columns that already exist:

| Legacy | New |
|---|---|
| `h1` ends with `'+'` | `StudentMarks.IsCarryForward = true` (not appearing, mark carried over) |
| `h1` has no `'+'` | `StudentMarks.IsCarryForward = false` (appearing, fresh attempt) |
| `h1` contains `'Ab'` | `StudentMarks.IsAbsent` |
| row exists for `exam_code` | `MarksMaster` for the target `ExamId` |
| `remark = 'UnSuccessful'` | `StudentSubjectResult.SubjectStatus`, or `SubjectPassEvaluator` when results are unprocessed |
| `'R' + exam_code` | `ExamMaster.RevaluationForExamId` |

**No new table is needed for the assignment itself.**

---

## 3. What was built (frontend, on disk, unverified)

| File | Status |
|---|---|
| `ExamClient/src/pages/Staff/ConductExam/AtktRevalExam.tsx` | 824 lines, complete, **never type-checked or run** |
| `ExamClient/src/services/AtktRevalExamService.ts` | 245 lines, **never type-checked** |
| `ExamClient/src/App.tsx` | **not wired** — no route added |
| `ExamClient/src/layouts/Staff/Sidebar.tsx` | **not wired** — the `"ATKT/Reval Exam"` entry still points at `/blank` |

Both files were written against the current `AtktRevalExamDto` contract and will need the
`AtktPolicyDto` shrink from §5 applied.

`AtktRevalMigration/AtktRevalExamServiceTests.cs.draft` — ~920 lines of xUnit scaffolding that
**does not compile** (its `World` fixture is used in a `using` statement without implementing
`IDisposable`). Parked here rather than in `ExamAPI.Tests/` so it cannot break the build. The
seeding helpers are worth salvaging when the tests are rewritten against the RuleSet design.

---

## 4. The only edits to existing, working code

Both are already made and covered by the green baseline in §1.

1. **`Services/Result/ResultService.cs`** — `CompareValues` now delegates to
   `RuleConditionEvaluator.Compare`. The switch expression was moved verbatim; the operator
   vocabulary (symbol and word forms) is byte-identical. Extracted so exam assignment evaluates
   rule conditions exactly the way result processing does, rather than growing a second
   interpretation.

2. **`Services/Exam/ExamService.cs`** — `CreateExamAsync` now assigns
   `exam.ExamId = Guid.NewGuid()` before adding the revaluation twin. Previously
   `RevaluationForExamId = exam.ExamId` was read **before** `SaveChangesAsync`, so the PK was
   still `Guid.Empty` and **every revaluation exam ever created was stored unlinked**.
   Revaluation assignment cannot resolve its target exam without this.

   > Existing rows are still broken. Any `ExamMaster` with
   > `RevaluationForExamId = '00000000-0000-0000-0000-000000000000'` needs a manual backfill
   > pointing it at its parent exam.

---

## 5. Decided next: drop the policy table, use the Ordinance engine

`ExamAssignmentPolicy` duplicated capability the rule engine already has. Every column maps
onto an existing construct:

| Policy column | Existing engine construct |
|---|---|
| `Mode` | `RuleSet.ExamType` — `Ordinance.tsx:139-143` already ships `Regular` / `KT` / `REEXAM` |
| `TargetExamTypes` | falls out — an exam is a valid target if its `ExamType` has an active RuleSet |
| `EligibleHeadTypes` | `RuleAction.Target` |
| `RequireFailedSubject`, `OfferPassedSubjects` | `RuleAction.Target` scope keywords (`FailingSubjects` vs `AllSubjects`) |
| `MaxSubjectsPerStudent` | `RuleAction.MaxTargetCount` |
| eligibility gate | `RuleCondition` + the `IFactProvider` registry |

Not config at all, and becoming fixed behaviour:
`CarryForwardMarks` (required for correctness — see §6), `BlockDeleteAfterMarksEntry` (an
invariant), `SubjectsPerRow` and `AutoSelectFailedSubjects` (UI constants).

**Resolution key:** `(request.Pattern, targetExam.ExamType)` → RuleSet. Pattern must come from
the screen, not the exam — `ExamMaster` has no `PatternId` and its `Semester` is never
populated. This matches what `ResultService` already does
(`rs.Pattern!.PatternName == request.Pattern`).

**The one genuine gap:** no `ActionType` expresses "may be assigned" — every `IActionHandler`
mutates a `MarksMaster` and returns void. Closed with a new ActionType string
`AllowExamAssignment` read directly by the assignment service. No new interface, no new table.
Safe to park in a KT rule set: `ResultService.cs:207-211` does `GetActionHandler(...)` then
`if (handler != null)`, so unknown action types are silently skipped by result processing.

**Revaluation has no exam type of its own** — `ExamService.CreateExamAsync` copies
`ExamType` onto the twin, so the reval of a Regular exam is also `"Regular"`. Resolve reval
rule sets via `RevaluationForExamId != null` → RuleSet with `ExamType = "REVAL"`, and add that
one entry to the Ordinance UI's `EXAM_TYPES`. No schema change, no backfill.

---

## 6. Two open correctness issues

### 6a. `RuleAction.Target` compares the wrong column

`SeedPharmacyBPharmSem6.sql:298-300` states the contract:

> Head is the positional key ('H1'/'H2') that `SubjectPassEvaluator.FindCredit` matches against
> `StudentMarks.Head`; HeadType is the printed label.

Every user-facing surface reads the **label**: gazette (`ReportService.cs:73` →
`GetHeadLabel`), marks-entry grid (`MarksEntryService.cs:83`), validation messages
(`MarksEntryService.cs:315,323,328`), hall ticket (`GenerateHallTicketService.cs:69` —
`HeadType.Contains("ESE")`). The Ordinance UI tells authors to type
*"the exact configured head name, such as ESE(TH)"*.

But both Target resolvers compare the **positional key**:

- `AddGraceHandler.cs:81` — `!headTargets.Contains(sm.Head.ToUpperInvariant())`
- `UpgradeGradeHandler.cs:42` — `targets.Contains(sm.Head.ToUpperInvariant())`

and they use two different keyword vocabularies (`IsAllFailingHeadsTarget` vs
`IsAllSubjectsTarget`). An author who follows the UI hint gets a filter matching nothing and an
action that silently no-ops. Undetected because every seeded rule uses the keyword
`'FailingSubjects'` (`SeedPharmacyBPharmSem6.sql:264`), never a real head name.

**Fix:** one shared `HeadTarget` resolver — keyword set (union of both vocabularies, so no
seeded rule changes meaning), otherwise normalised exact match against `GetHeadLabel(sm)` and
the raw `sm.Head`, so `ESE`, `ese` and `H1` all resolve.

> **Risk before switching the two handlers over:** any hand-authored rule in the live database
> with a real head name in `Target` is a no-op today and would **start firing**, changing result
> output. Query live `RuleAction.Target` values first. The resolver goes into the assignment
> service immediately; the handlers switch only after that check.

### 6b. Head-scoped re-attempt must carry unselected heads forward

`AtktRevalExamService.UpsertAssignment` currently sets `IsCarryForward = false` on **every**
head of a selected subject, blanking its marks.

Legacy only ever cleared the `'+'` on `h1` — no `H2_` checkbox was ever rendered. The real rule
is: re-sit the theory paper, the term-work mark stands.

Under **HeadWise** passing, blanking term work is merely wrong. Under **Combined** it corrupts
the verdict: `SubjectPassEvaluator.Evaluate` sums `Marks ?? 0`, so a blanked head silently
subtracts its contribution from the combined total and the student can fail on data loss alone.

Passing strategy is **per subject** (`SubjectCreditMaster.PassingStrategy`, written per
subject-credit row by `SubjectService.cs:111-112` on create and `365-366` on update), so one
exam mixes HeadWise and Combined subjects and this has to be right for both.

**Fix:** only heads named by the re-attempt scope get `IsCarryForward = false` and blanked
marks; every other head carries forward with its marks intact.

---

## 7. Deliberately out of scope

**Group / division scoping.** Legacy `cre_marks_tbl.extra2` holds a group id and scopes the
whole screen (`ddl_group`, `std_ext_qry`). There is no Group concept anywhere in the new
schema, models, services or client — the only trace is an unused `groupId?: string` in
`ReportService.DownloadGazetteParams`. Adding it is a cross-cutting feature, not part of this
form.

**Legacy dead paths.** `ddl_pattern` only ever offered `R-2024`, so `load_grd()`,
`btn_save_Click`, `lnk_delete_Click`, `btn_yes_Click` and the GridView pipeline were
unreachable in the live build. Their head-wise threshold and ESE-only-reval rules are captured
above; the code itself is not being ported.

---

## 8. Remaining work

1. Add the `HeadTarget` resolver (§6a), assignment service only.
2. Remove `ExamAssignmentPolicy` + migration + seed SQL; rewire policy resolution to RuleSet (§5).
3. Fix the head-scoped write (§6b).
4. Add `REVAL` to the Ordinance UI `EXAM_TYPES`.
5. Type-check and wire the React page (route + sidebar path).
6. Rewrite the tests against the RuleSet design, salvaging the draft's fixtures.
7. Re-run the §1 baseline; manually exercise marks entry → result → gazette once.
8. Backfill `RevaluationForExamId = Guid.Empty` rows (§4).
