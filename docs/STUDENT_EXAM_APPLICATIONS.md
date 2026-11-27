# Student exam applications: apply and pay for ATKT, Re-exam and Revaluation by link

**Status:** plan, agreed with the owner 2026-11-27. Nothing built yet. Tasks: TASKS.md T-78 … T-80.

**Goal:** staff open applications for an ATKT, Re-exam or Revaluation exam and email each eligible student a link.
The student confirms it's them with the 6-digit code, picks subjects, sees the fee, pays online, and is put into the
exam automatically. They get a receipt and an exam form. Staff see who applied and paid, and get a fee report.

## Owner decisions (2026-11-27)

| # | Decision |
|---|---|
| D1 | **Gateway not chosen yet.** Build everything behind a gateway interface, with a test gateway for development; the real provider is one adapter, added once chosen. |
| D2 | **Revaluation is in the first release**, alongside ATKT and Re-exam. |
| D3 | **Fees are defined by staff** per exam, with a clear, structured screen (not hard-coded like the old ₹60). |
| D4 | **After payment the student is assigned to the exam automatically** (no staff approval), like the old system. |
| D5 | **A gateway account per college plus a platform default**, set up in the Platform portal like the email accounts. |

## 1. What the old system did (`OldProjects/60_40_LTCE`)

- Student logged in (ID + plain-text password), saw exams staff had switched on for the class
  (`frm_SemAndExamActive` → `cre_Semester_Exam_data`), in three kinds: ATKT (`std_atkt_exam`), Re-exam (`std_reExam`), Regular.
- Failed subjects were pre-ticked; the student could untick. Already-paid subjects were locked.
- Fee = ticked subjects × rate for the student's category (`cre_fess_define`) + ₹60 statement of marks (hard-coded).
- Payment through SabPaisa (AES-encrypted redirect, `payment.aspx`); pending payments re-checked with SabPaisa on the
  student's next visit (`Class1.SabPaisaRequery`).
- On success the student was put into the exam automatically (marks rows created), receipt + printable exam form.
- Staff: fee definition screen, fee report with Excel export. No approval, no refunds, no deadlines, no late fee.
- **Revaluation had no student flow at all**: staff-only. So the revaluation application here is new.

**Do not copy:** the amount came from the browser and was never compared with what was due; "paid" was set from the
return page without asking the gateway; the receipt page accepted unauthenticated posts (anyone with a valid response
could act as the student); the date-range fee report only counted re-exams; receipt/txn numbers were built from row
counts (collide after deletes). The old source also has **live SabPaisa keys written into the code** (`payment.aspx.cs`,
`App_Code/Class1.cs`): never copy them; if SabPaisa is chosen, ask them for new keys and enter them in the Platform portal.

## 2. What the new system already has (reuse, don't rebuild)

| Need | Already there |
|---|---|
| Emailed link, 6-digit code, student session scoped to one student/college/task | `Services/StudentAccess/*` (live for Student Master and hall tickets). Copy the hall-ticket pattern described in `docs/STUDENT_LINKS_MARKSHEET.md` §1. |
| Task type for this | `TaskTypes.AtktPayment = "ATKT_PAYMENT"` row is already seeded on live ("Pay ATKT exam fee", `DIE_ON_PAYMENT`). Reuse the code for all three kinds; rename it to "Apply for exam" and switch it to `DIE_ON_DEADLINE` in the migration (the student must come back for the receipt). |
| Who may sit which subject | `AtktRevalExamService.GetMatrixAsync`: failed subjects per student from the latest attempt, per-head scope from the rule set, revaluation subjects from the chosen source exam. **The student page must use this, not its own rules.** |
| Putting a student into the exam | `AtktRevalExamService.SaveAsync` (creates the target `MarksMaster`, carries forward passed subjects/heads). Revaluation reuses the parent's seat numbers (DEC-16). |
| Per-college secrets with a platform default | `EmailAccount` (`CollegeId` null = default), `SecretProtector`, `PlatformEmailController`, Platform portal email page. Copy for gateway accounts. |
| Email sending, background worker | `EmailQueue` + background sender. Add the receipt email as a new kind. |
| PDFs | QuestPDF, as in hall tickets / staff letters. |

**Gap to fix first:** Exam Master offers "Re-Exam", but the assign service only accepts `ATKT`/`KT` as target types
(`AtktTargetExamTypeKeys`). Add `REEXAM`, check that the result, gazette and ordinance paths handle a Re-Exam exam
(Ordinance already lists "REEXAM"), and add tests.

## 3. Data (one migration, new tables only)

- **`PaymentAccount`** (copy of `EmailAccount`): `CollegeId` (null = platform default), `Provider` (e.g. `TEST`, later the
  chosen gateway), `MerchantId`, `SecretsProtected` (JSON of the provider's keys, encrypted by `SecretProtector`, never
  returned to a screen), `IsTestMode`, `IsEnabled`, `UpdatedAt/By`. A college uses its own enabled account, else the default.
  Note: with the default account the money settles into that merchant's bank account. Keep the default for VIVA, or for
  colleges that agree to it in writing.
- **`ExamFeeSetup`** (one per target exam): `ExamId`, `Kind` (ATKT / REEXAM / REVAL, from the exam), `OpensAt`, `ClosesAt`,
  `LateFeeFrom`, `LateFee`, `PerSubjectFee`, `FixedFee` + `FixedFeeLabel` (e.g. "Statement of marks"), `MaxFee` (optional
  cap), `Notes` (shown to the student).
- **`ExamFeeCategoryRate`** (optional rows under a setup): `Category`, `PerSubjectFee`, `FixedFee`, `IsExempt`. Exempt =
  fee ₹0, no gateway, recorded as "Fee waived".
- **`ExamApplication`**: `StdMstId`, `TargetExamId`, `SourceExamId` (revaluation), `TaskLinkId`, `Kind`, `Status`
  (Draft → PaymentPending → Paid → Assigned; also AssignFailed, Cancelled, Refunded), `FeeBreakdownJson`, `AmountDue`,
  `ReceiptNo`, `PaidAt`, `PaidVia` (Online / Office / Waived), `AssignedAt`, `AssignError`. One open application per
  student per target exam (unique filtered index).
- **`ExamApplicationSubject`**: `SubjectId`, heads chosen (for head-wise subjects).
- **`PaymentOrder`** (many per application: retries): `OrderNo` (unique, random, not count-based), `Amount`,
  `PaymentAccountId`, `Provider`, `Status` (Created / Pending / Success / Failed / Expired), `GatewayTxnId`, `Mode`, `Bank`,
  `RawStatus`, `CreatedAt`, `ConfirmedAt`, `LastCheckedAt`, `CheckCount`.
- **`ReceiptCounter`**: per college + academic year + kind, for numbers like `ATKT/2026-27/000123`.

## 4. Payment rules (non-negotiable)

1. The **server** works out the amount from the fee setup when the order is created and stores it on the order. The
   browser never sends an amount, rate or subject list that is trusted.
2. A payment counts **only after the server asks the gateway** (server-to-server status call) and the answer matches the
   order number **and** the amount. The return page and any webhook only trigger that check.
3. **Idempotent:** confirming an already-successful order does nothing; the Paid → Assigned step runs once (status change
   inside a transaction). A second successful order for the same application is flagged "Paid twice: refund" for staff.
4. A background job re-checks Pending orders (every 5 minutes for 48 hours, then Expired).
5. Gateway keys live only in `PaymentAccount` (encrypted). Nothing in code, `appsettings`, logs or emails.
6. The test gateway is refused when the environment is Production.

**Gateway interface** (`Services/Payments/IPaymentGateway.cs`): `StartAsync(order, account)` → redirect URL or auto-post
form; `ReadReturn(request)` → order number only; `GetStatusAsync(orderNo, account)` → status, amount, gateway txn id, mode,
bank. When the owner picks a provider: one adapter class + tests, then a ₹1 live test. **Choose a provider that has a
status-enquiry API and a test mode** (SabPaisa, the old one, has both).

## 5. Screens

**Staff (Exam module):**
- **Exam Fees:** step layout like Student Master: 1. pick the exam (ATKT / Re-exam / Revaluation) → 2. fees and dates (per
  subject, fixed fee with label, category rows, late fee, cap) → 3. preview ("2 subjects, OPEN → ₹X; SC exempt → ₹0").
  Locked once a payment exists (edits then need a new setup version, or only the closing date can move).
- **Exam Applications:** pick the exam → counts (eligible, link sent, opened, applied, paid, assigned, failed) → table per
  student → Send links / Resend to those who haven't paid (like Email Hall Tickets) → per row: "Paid at office" (receipt
  ref + reason, audited), "Retry assign", "Cancel", "Mark refunded" (manual note; no gateway refunds in v1) → Excel.
- **Fee Report:** by exam, kind or date range, all kinds, totals, order no + gateway txn id for matching against the
  gateway's settlement report, Excel.

**Platform portal:** **Payment gateway** page beside the email page: default account + per college, test mode switch,
"Test order (test mode only)".

**Student (link):** code → subjects (ATKT/Re-exam: failed ones pre-ticked, may untick; Revaluation: subjects of the source
exam, none pre-ticked) → fee breakdown from the server → Pay → result page (Paid / Failed, try again / Pending, being
checked) → after paying: receipt PDF + exam form PDF, also emailed. Link works until the closing date.

Admins see the new screens automatically; add them to `screens.ts` so Role Master can tick them for other roles.

## 6. Build order

| Step | What | Who | Size |
|---|---|---|---|
| 0 | Re-Exam as an assign target + tests (gap in §2) | Claude | S |
| 1 | Migration (§3), fee setup service + Exam Fees screen | Claude (model) / Nitesh (screen) | M |
| 2 | Payment core: orders, rules §4, test gateway, background re-check, `PaymentAccount` + Platform page | Claude | L |
| 3 | Exam Applications: send links, status list, office payment, retry assign | Nitesh from this guide | M |
| 4 | Student page + automatic assignment via `SaveAsync` + receipt and exam form PDFs + receipt email | Claude (assign/receipt) / Nitesh (page) | M |
| 5 | Fee Report | Nitesh | S |
| 6 | Real gateway adapter once chosen; ₹1 test on live | Claude + owner | S |
| 7 | Live update script, rehearsal on a scratch DB, one real class | Claude + owner | S |

Tests to have before live (copy `HallTicketLinkTests.cs` / `BillingClaimLinkTests.cs`): amount from browser ignored;
unconfirmed return doesn't mark paid; amount mismatch refused; double confirm assigns once; another college's student or
exam unreachable; closed window refuses; exempt category skips the gateway; reval only offers the source exam's subjects.

## 7. Open questions (ask before the step that needs them)

- Revaluation extras: a photocopy-of-answer-book fee or step before revaluation? (Not in v1 unless asked.)
- Re-exam vs ATKT naming on receipts and the exam form: confirm wording with the exam office.
- Regular exam fees by link (old `std_regul_exam`): out of scope for now.
