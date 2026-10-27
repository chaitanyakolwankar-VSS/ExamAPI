# Staff letters

A guide for the exam office and for developers. Staff Master (step 1) and Supervision Letters (step 2) are done.

## 1. What it is

Staff letters are the letters the exam office sends to staff. The first kind is the **supervision letter**, which tells a person which exam duties they have and when. Examiner and moderator appointment letters come next. Every letter has a number. The people who get letters are kept in **Staff Master**, and duties are given on the **Supervision Letters** screen. Letters are not yet made as PDFs or emailed.

## 2. Staff Master

Open it from **Staff Admin > Staff Master**. It has three tabs: **Staff**, **Excel import**, and **Departments & designations**.

### Who goes in

- **Our own staff.** Each one needs an employee ID.
- **External examiners.** Professors of other colleges. Tick **External** and enter their college (and city). An employee ID is optional for them.

Everyone needs a title, a first name and an email. Letters and one-time codes go to that email, so use the person's own address. An employee ID and an email each belong to one person only.

### The Excel sheet

Download it from the **Excel import** tab. Row 1 is a title, row 2 has the headings, and data starts on row 3.

| Column | Needed? | Notes |
|---|---|---|
| EmployeeId | Yes, for our own staff | Optional for external staff. |
| Title | Yes | Drop-down: Prof., Dr., Mr., Mrs., Ms., Miss. |
| FirstName | Yes | MiddleName and LastName are optional. Up to 50 characters each. |
| Email | Yes | A valid address, unique in the college. |
| Department, Designation | No | Drop-downs. A new name is added to the list on import. |
| External | No | Yes or No. Empty means No. |
| ExternalCollege | Yes, if External is Yes | Up to 200 characters. |
| ExternalCity | No | Up to 100 characters. |

### Importing

1. Download the sheet. It lists everyone still at the college, so the same sheet adds new people and updates existing ones.
2. Fill it in and upload it. Each row is shown as **new**, **updated** or **unchanged**. Nothing is saved yet.
3. If any row has a problem, nothing is imported. The list shows the row numbers. Fix them and upload again.
4. Press **Save staff**.

Rules:

- Empty rows are skipped. An employee ID or email cannot appear twice in one sheet.
- A row finds its person by **employee ID first**, then by **email**.
- A row that matches someone marked as left makes them active again.
- People who are not in the sheet are not changed.

The **Staff** tab edits one person at a time, with the same checks. Search covers name, email and employee ID.

### Departments and designations

- There are two lists, and staff pick from them.
- On the **Departments & designations** tab you can add, rename or delete items. A name can appear only once in each list.
- An item that staff use cannot be deleted. The message says how many people use it.

### Still at the college, or delete

- **The person has left:** edit them and untick **Still at the college**. Their old letters are kept, and they are not offered for new ones.
- **Entered by mistake:** delete them. Use delete only for mistakes.

## 3. Supervision letters


### What "the exam" means

Duties are given for the exam as a whole, with all branches together. An exam is every branch's copy of the same exam: same name, exam type and academic year. Revaluation exams are not listed.

For each exam the screen shows:

- **Half.** "First Half" if every timetabled subject is in an odd semester, "Second Half" if every one is even. Mixed, or none, leaves it blank.
- **Slots.** One date and start time from the timetable, across all branches, in exam order. Each slot is a column in the grid.

Letters are always stored against one copy of the exam, whichever branch the screen was opened from.

### Assigning in the grid

- Rows are our own active staff. External examiners are not listed.
- Pick a **Role**: Senior Supervisor, Junior Supervisor, Chief Conductor or Peon.
- Tick the slots the person will supervise. Ticking a slot on a row with no role sets **Junior Supervisor**.
- A person counts as **assigned** when they have a role and at least one slot.
- A slot's header checkbox ticks or unticks that slot for the visible rows that have a role.
- The summary reads "{n} staff assigned, {m} duties". Each slot header shows how many staff are on it.
- The **letter date** (default today) is used only for new letters.

### Saving

**Save letters** sends the whole grid for the exam. Saving is all or nothing.

| What you did | What happens |
|---|---|
| Assigned a person who had no letter | A new letter, with the next number and the letter date. |
| Changed the role or slots of a person who has a letter | Same letter and number. Role and duties are updated. The date is kept. |
| Cleared a person (no role, or no slots) who has a letter | The letter is cancelled. Its number is never used again. |

- If any letter will be cancelled, the screen asks you to confirm and lists the names.
- Until you save, the screen shows **Unsaved changes**. It also warns you if you change the exam while there are unsaved changes.
- Being assigned is final. Staff cannot accept or decline on the screen.
- If a check fails, nothing is saved. The message says what is wrong, such as a role not in the list, a slot not in this exam, an inactive or external person, or the same person twice.

### Letter numbers

Format: `{prefix}/{code}/{year}/{number}`. Example: `VIT/EXAM/SUP/2026-27/0042`

| Part | Value |
|---|---|
| prefix | The Letter settings prefix. If none is set: the college code and `/EXAM`, e.g. `ENG001/EXAM`. |
| code | `SUP` for supervision. `EXM` and `MOD` are for examiner and moderator letters. |
| year | The academic year, e.g. `2026-27`. |
| number | Four digits. Counts up for each college, letter type and academic year. |

Numbers are never reused. A cancelled letter keeps its number, and the next new letter takes the one after the highest so far.

### Letter settings (admins only)

Shown to college and platform admins. Other users see the current prefix, read-only.

- **Prefix:** up to 40 characters: letters, digits, `/`, `-` and `.` only. Empty means the default, shown as the placeholder.
- **CAP contact:** the CAP contact numbers, saved with the prefix.

## 4. Roadmap

| Step | What | Status |
|---|---|---|
| 1 | Staff Master: our staff and external examiners, Excel import, departments and designations | Done |
| 2 | Letter tables, numbering, and the Supervision Letters screen | Done |
| 3 | Examiner and moderator appointment letters | Planned |
| 4 | Letters made as PDFs on the server | Planned |
| 5 | One emailed link per staff member per exam, with a one-time code. A short form first if details are missing. Then that exam's letters, earlier exams collapsed. Separate tables from student links. | Planned |
| 6 | Office tracking (sent, opened, downloaded) and the Resolution Report certificate, using the staff list | Planned |

## 5. For developers

Letters copy the staff name, exam title and half onto the letter row, so a re-download prints the same as the first time.

| Area | Files | Step |
|---|---|---|
| Models | `ExamAPI/ExamAPI/Models/Staff.cs`; `StaffLetter.cs` (`StaffLetter`, `StaffLetterDuty`, `StaffLetterTypes`); `StudentAccess.cs` (`CollegeSettings`) | 1; 2; 2 |
| EF config, DbContext | `ExamAPI/ExamAPI/Data/StaffModel.cs`; `Data/ApplicationDbContext.cs` | 1 (letters: 2) |
| Migrations | `ExamAPI/ExamAPI/Migrations/20261027062147_AddStaffMaster.cs`; `AddStaffLetters` (not yet created) | 1; 2 |
| Services | `Services/Staff/StaffService.cs` (staff, lists, Excel); `Services/Common/ExamGroups.cs`; `Services/Common/ExamSlotOrder.cs`; `Services/Staff/StaffLetterService.cs` (exams, grid, numbering, letter settings) | 1; 2; 2; 2 |
| Shared with existing code | `Services/GenerateHallTicket/GenerateHallTicketService.cs` (`GetTimeTableAll`); `Services/AttendenceReport/AttendenceReportService.cs` | existing |
| Seeder | `ExamAPI/ExamAPI/Services/Platform/StartupSeeder.cs` (`Screens`) | 2 |
| Controllers | `ExamAPI/ExamAPI/Controllers/StaffController.cs`; `StaffLettersController.cs` (route `api/StaffLetters`) | 1; 2 |
| API tests | `ExamAPI/ExamAPI.Tests/StaffServiceTests.cs`; `StaffLetterTests.cs`; `GenerateHallTicketTimeTableTests.cs` and `AttendenceReportTests.cs` (must keep passing) | 1; 2; existing |
| Client pages | `ExamClient/src/pages/Staff/Staff Admin/StaffMaster.tsx`, `StaffOptionLists.tsx`, `SupervisionLetters.tsx` | 1; 1; 2 |
| Client shared | `ExamClient/src/pages/Staff/Students Admin/SheetImportPanel.tsx` (Excel import panel) | existing |
| Client services, config, routing | `ExamClient/src/services/StaffService.ts`, `StaffLetterService.ts`; `src/config/screens.ts` (path `/Staff/SupervisionLetters`); `src/App.tsx` (lazy route) | 1; 2; 2; 2 |

Checks before a change goes in:

- API tests: `dotnet test ExamAPI.Tests -o <scratch folder>`. Use `-o` because Visual Studio can lock `bin`.
- New migration: check that it only adds tables and columns before applying it.
- Client: `npx tsc -b --noEmit` must be clean, then `npx vite build --outDir <scratch folder> --emptyOutDir`.
