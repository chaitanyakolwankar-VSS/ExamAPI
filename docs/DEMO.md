# Demo database (DBExamAPI)

`DBExamAPI` on the dev server (100.121.68.85) is the **demo** database: an engineering college and a pharmacy
college, ready to show the student side (details link and hall ticket by link). It was cleaned with
`_review/DemoCleanup.sql` on 23 Oct 2026.

## What's in it

| College | Code | Demo exam (hall tickets ready) | Demo student |
|---|---|---|---|
| VIVA Institute of Technology (engineering) | ENG001 | T.E. Sem-VI Regular Exam May 2025, Computer Science, 69 students, timetable 1–9 Dec 2026 | 6242011 |
| Pharmacy College | PHM001 | B.Pharm Sem-VI Regular Exam, 30 students | PH2501 |

- Only the two demo students have an email (Chaitanya's college email). Every other email is gone or ends in
  `@demo.invalid`, which can never be delivered, so a demo can't email a real person.
- The demo students have no photo or signature, so the demo shows adding them.
- There is **no mail account** yet. Before a demo, the platform admin enters the demo's own sender (not the live
  one) under email settings. Nothing is emailed until then.

## Staff sign-in

Staff sign in with their email, and the emails were changed to `@demo.invalid`. Passwords are unchanged.

| College | User | Sign in with |
|---|---|---|
| Platform | platform.admin | `platform.admin.platform@demo.invalid` |
| ENG001 | admin | `admin.eng001@demo.invalid` |
| PHM001 | admin | `admin.phm001@demo.invalid` |
| PHM001 | staff.pharm | `staff.pharm.phm001@demo.invalid` |
| TST001 (test college) | admin, chaitanya_test | `admin.tst001@demo.invalid`, `chaitanya_test.tst001@demo.invalid` |

Forgot password doesn't work here (no real emails). Reset a password from the user screens instead.

## Demo script (about 10 minutes per college)

1. **Details link.** Staff: Student Master → Student links → One student → the demo student → choose what they can
   change (e.g. everything except the four names) → Send. Student (open the email on the demo PC or phone): open the
   link, get the code, add photo and signature (see the guide under the card), fill in the details, Save. Show the
   "Saved" box and the email listing what changed. Staff: Student links shows "1 of 1 updated" and what changed.
2. **Hall ticket link.** Staff: Generate Hall Ticket → course, pattern, semester, the demo exam → Email hall tickets →
   One student → the demo student. Student: open the link, get the code, the hall ticket shows with the timetable,
   ERN, ABC ID, photo and signature, **Download PDF**. Staff: "1 of 1 downloaded"; the dashboard shows the exam's hall
   tickets as released.
3. **What a forwarded link shows.** Open the link in a private window: only the first name and masked email; nothing
   opens without the code from the student's inbox.

To demo again from scratch, re-run `_review/DemoCleanup.sql` (preview, then `@Apply = 1`): it clears the links, the
demo students' pictures and any new emails.
