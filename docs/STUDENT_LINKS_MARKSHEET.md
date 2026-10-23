# Marksheets by student link (guide for Nitesh)

**Goal:** after a result is declared, staff email each student a link. The student confirms it's them with the
one-time code and downloads their own marksheet as a PDF. It works exactly like hall tickets by link, which is
already built and live. Copy that feature piece by piece; most of the work is renaming and one new service.

Read this whole page first, then build in the order of the checklist at the end.

---

## 1. How hall tickets by link work today (the model to copy)

```
Staff: Generate Hall Ticket ──POST api/StudentLinks/hallticket──▶ TaskLinkService.SendHallTicketsAsync
   (EmailHallTickets.tsx panel)                                    one TaskBatch (RefId = ExamId) + one TaskLink per student
                                                                   emails queued in EmailQueue (sent by the background sender)
Student opens email ──/s#token──▶ StudentPortal.tsx ──open / code / verify──▶ StudentSignInService (6-digit code, session)
   home.taskType = "HALL_TICKET" ──▶ StudentHallTicket.tsx ──GET api/StudentAccess/hallticket──▶ StudentHallTicketService
   "Download PDF" ──POST api/StudentAccess/hallticket/downloaded──▶ link.UsedAt (= downloaded), audit
Staff follow progress: Student Master > Student links (StudentLinks.tsx), "X of Y downloaded"
```

| Piece | Hall ticket file | What it does |
|---|---|---|
| Send + preview | `ExamAPI/Services/StudentAccess/TaskLinkService.cs` (`SendHallTicketsAsync`, `PreviewHallTicketsAsync`, `HallTicketLinksRequest`, `HallTicketSendPreview`) | Picks the students (a `MarksMaster` row for the exam), refuses if not ready, queues one batch |
| Staff endpoint | `ExamAPI/Controllers/StudentLinksController.cs` (`POST hallticket`, `POST hallticket/preview`) | Staff only (JWT, college from token) |
| Student service | `ExamAPI/Services/StudentAccess/StudentHallTicketService.cs` | Everything comes from the signed-in **link**, never from ids sent by the browser |
| Student endpoints | `ExamAPI/Controllers/StudentAccessController.cs` (`GET hallticket`, `POST hallticket/downloaded`) | `[Authorize(Policy = StudentAccessAuth.Policy)]` |
| Emails | `ExamAPI/Services/StudentAccess/StudentLinkEmails.cs` (`Purpose`, `Steps`, button text; `TaskLinkEmailRenderer` loads the exam name from `RefId`) | Link email wording per task |
| Closed-link message | `StudentSignInService.cs` (search `TaskTypes.HallTicket`) | What a student sees on an expired or stopped link |
| Staff panel | `ExamClient/src/pages/Staff/Reports/EmailHallTickets.tsx` | Everyone / one student, days, preview, resend to those who haven't downloaded |
| Student page | `ExamClient/src/pages/StudentSite/StudentHallTicket.tsx`, routed in `StudentPortal.tsx` by `home.taskType` | |
| API clients | `ExamClient/src/services/StudentLinksService.ts` (staff), `StudentAccessService.ts` (student) | |
| Tests | `ExamAPI.Tests/HallTicketLinkTests.cs` | Copy this file for marksheets |

**Already in place for you:** `TaskTypes.Marksheet = "MARKSHEET"` (`Models/StudentAccess.cs`) and its `TaskType` row
("Download marksheet", seeded by the `AddStudentAccess` migration). No migration is needed for the task type.

**The marksheet PDF already exists server-side:** `IReportService.GenerateMarksheetPdfAsync(studId, examId, semId,
pattern, includeHistory, resultDate, collegeId, noRleForFail)` in `Services/Report/ReportService.cs` (QuestPDF). It
only reads, so the student endpoint can call it directly and return the bytes. That makes the student side simpler
than hall tickets: no HTML capture, the browser just downloads the PDF.

Do **not** call `GenerateBulkMarksheetPdfAsync` from the student side: it writes a "marksheet run" count on
`DeclareResult` (it feeds Declare Result).

---

## 2. What to build

### Backend

1. **Request, preview, send** in `TaskLinkService.cs`, next to the hall ticket ones:
   - `MarksheetLinksRequest { Ayid, CourseId, Semester, Pattern, ExamId, Student?, OnlyNotDownloaded, Days = 30, AppUrl }`.
   - Students: same as hall tickets (`MarksMaster` rows of the exam), one student when `Student` is set.
   - **Ready check:** refuse unless the result is declared for that exam, course, semester, pattern and year
     (`DeclareResult.IsDeclare`; read it with `DResultService.RowsByExam`, as the old hall ticket check did; see git
     history of `SendHallTicketsAsync`). Message: "Declare the result for this exam first."
   - Batch: `TaskTypeCode = TaskTypes.Marksheet`, `RefId = exam.ExamId`, `EditableFields = ""` (view only),
     title "Marksheet — {exam} — {class}". Then `QueueBatchAsync` (it stops each student's earlier marksheet link for
     the same exam).
   - Preview: copy `PreviewHallTicketsAsync` (students, sendable, no email, flagged, emailed, downloaded); drop the
     timetable part, add "result declared yes/no".
2. **Marksheet options:** the staff Marksheet screen has *include history*, *result date* and *no RLE for fail*.
   Decide them at send time and keep them on the batch so every student gets the same marksheet: add a nullable
   `Options` column (JSON text) to `TaskBatch` (migration `AddTaskBatchOptions`). Default `resultDate` to the
   declare date. Ask Chaitanya before choosing different defaults.
3. **Staff endpoints** in `StudentLinksController.cs`: `POST marksheet` and `POST marksheet/preview`, copied from
   the hall ticket ones (same `Answer(...)` wrapper, same `_site.Resolve(request.AppUrl)`).
4. **Student service** `Services/StudentAccess/StudentMarksheetService.cs`, shaped like `StudentHallTicketService`:
   - `LoadAsync(user)`: link from the session claim, must be open, must be `TaskTypes.Marksheet` with a `RefId`.
   - `GetAsync(user)`: exam name, college, ready or a message ("Your result isn't declared yet" / withdrawn).
     Re-check the declare flag every time: a result can be withdrawn after sending.
   - `PdfAsync(user)`: calls `GenerateMarksheetPdfAsync(student.StdMstId, exam.ExamId, batch.SemesterId, pattern,
     options…, collegeId: link.CollegeId)`. Pattern comes from the student's `MarksMaster` row, as in the hall
     ticket service.
   - `MarkDownloadedAsync(user)`: first download only → `link.UsedAt`, audit `MarksheetDownloaded`.
   - Register it in `Program.cs` next to `IStudentHallTicketService`.
5. **Student endpoints** in `StudentAccessController.cs`: `GET marksheet` (the view), `GET marksheet/pdf`
   (`File(bytes, "application/pdf", "Marksheet {StudentId} {Exam}.pdf")`, and record the download there), all with
   `[Authorize(Policy = StudentAccessAuth.Policy)]`.
6. **Emails** in `StudentLinkEmails.cs`: `Purpose` ("download your marksheet for {exam}"), `Steps` (confirm with the
   code, download the PDF and keep it), button "Open my marksheet"; in `TaskLinkEmailRenderer` load the exam name
   for marksheets too (today only for `HallTicket`). Closed-link text in `StudentSignInService`.

### Frontend

1. `StudentLinksService.ts`: `sendMarksheets` and `previewMarksheets`, copied from the hall ticket pair.
2. **Staff panel:** `EmailMarksheets.tsx` (copy `EmailHallTickets.tsx`) shown on `ExamClient/src/pages/Staff/Reports/Marksheet.tsx`
   once branch, pattern, semester and exam are chosen. Show "Declare the result first" instead of the send button
   while it isn't declared.
3. **Student page:** `StudentMarksheet.tsx`, routed in `StudentPortal.tsx` when `home.taskType === "MARKSHEET"`.
   Title, exam name, one **Download PDF** button: fetch `GET /StudentAccess/marksheet/pdf` with the student client
   (`responseType: "blob"`, the session header is added for you) and save it with an object URL. No print window.
4. `StudentLinks.tsx`: today `isHallTicket` decides "downloaded" wording; make it cover `MARKSHEET` as well.

### Tests (`ExamAPI.Tests/MarksheetLinkTests.cs`, copy `HallTicketLinkTests.cs`)

- Not declared → send refused; declared → one link per student, batch has `RefId` = exam.
- Withdrawn after sending → the student sees a message, no PDF.
- Stopped or expired link → 401.
- A student only ever gets their own marksheet (ids come from the session, nothing from the request).
- First download sets `UsedAt` and writes one audit row; later downloads don't.
- Mock `IReportService` in tests (return a few bytes) so you test the link logic, not QuestPDF.

Run `dotnet test` (all must pass) and `npx tsc -b --noEmit` in ExamClient before every commit.

---

## 3. Security rules (don't skip)

- On the student side **never accept** a student id, exam id, semester or pattern from the browser. Everything comes
  from `link → batch → RefId`.
- Every student endpoint goes through the link check (`StudentAccessAuth.IsOpen`) and the `Student` policy; staff
  endpoints stay on the normal staff JWT and are tenant-filtered by `CollegeId`.
- Don't log or store tokens; the email link is `/s#token` (the token never reaches the server log).

---

## 4. Working locally

- **Code:** everything here is on GitHub: `master` of `chaitanyakolwankar-VSS/ExamAPI` and `main` of
  `chaitanyakolwankar-VSS/ExamClient`. Pull both, then work on your own branch from them (e.g.
  `nitesh/marksheet-links`) and open a pull request when a piece is done.
  ```bash
  git checkout master && git pull && git checkout -b nitesh/marksheet-links   # ExamAPI (use main in ExamClient)
  ```
- **Seeing it working first:** the demo (`https://www.vivacollege.in/ExamSoftware`, database `DBExamAPI`) runs this
  code. Ask Chaitanya for a demo staff sign-in and try a hall ticket link end to end before you start; see `DEMO.md`.
- **Database:** use your own `DBExamAPI_Nitesh`. It needs every migration up to `AddAbcIdAndCollegeSeal`:
  set your connection string in `appsettings.Development.json` (don't commit it) and run
  `dotnet ef database update` from `ExamAPI/ExamAPI`.
  Don't use `DBExamAPI`: it is now the **demo** database (see `DEMO.md`). Never connect to the live database.
- **Email:** emails only go out when a mail account is set (Platform admin → email settings). Use a test mailbox of
  your own; send links only to your own email.
- **Run:** API `dotnet run --project ExamAPI/ExamAPI --launch-profile https` (https://localhost:7225);
  client `npm run dev` in ExamClient (http://localhost:5173/ExamSoftware). Email links point to the client you
  sent them from, so open them on the same PC.
- **Commits:** one line, plain English, what changed (e.g. "Marksheet by link: staff send panel and student
  download"). Small commits, one piece at a time.

---

## 5. Checklist (build in this order)

- [ ] `MarksheetLinksRequest`, preview and send in `TaskLinkService` + tests
- [ ] `TaskBatch.Options` migration, options saved at send time
- [ ] Staff endpoints `POST marksheet`, `POST marksheet/preview`
- [ ] `StudentMarksheetService` + student endpoints + `Program.cs` + tests
- [ ] Email wording, renderer exam name, closed-link text
- [ ] `StudentLinksService.ts` staff calls
- [ ] `EmailMarksheets.tsx` on the Marksheet screen
- [ ] `StudentMarksheet.tsx` + routing in `StudentPortal.tsx`
- [ ] "downloaded" wording in `StudentLinks.tsx`
- [ ] End-to-end on your database with your own email: declare, send, open link, code, download
