using System.Globalization;
using System.Text.RegularExpressions;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Staff
{
    public interface IStaffLetterService
    {
        Task<List<ExamGroupDto>> ExamsAsync(Guid ayid);
        Task<SupervisionDto> SupervisionAsync(Guid examId);
        Task<SupervisionSaveResult> SaveSupervisionAsync(SupervisionSave dto);
        Task<AppointmentsDto> AppointmentsAsync(Guid examId);
        Task<AppointmentSaveResult> SaveAppointmentsAsync(AppointmentSave dto);
        Task<LetterSettingsDto> GetSettingsAsync();
        Task<LetterSettingsDto> SaveSettingsAsync(LetterSettingsSave dto);
    }

    /// <summary>
    /// Letters to staff. Supervision letters: the exam office gives each member of staff a duty role and the dates
    /// they supervise for one exam (all branches together); the screen sends the whole grid and the letters follow it.
    /// Appointment letters: the exam office appoints an examiner and a moderator for each subject of an exam, and the
    /// screen sends all of them at once. Every letter gets a number that is never reused, even when the letter is cancelled.
    /// </summary>
    public class StaffLetterService : IStaffLetterService
    {
        private readonly ApplicationDbContext _context;

        public StaffLetterService(ApplicationDbContext context)
        {
            _context = context;
        }

        // ------------------------------------------------------------------ exams and slots

        /// <summary>One date and time papers are written, with the number of papers and the branches writing them.</summary>
        private sealed record Slot(string Date, string Time, int Papers, List<string> Branches);

        /// <summary>An exam group: the canonical copy, all copies, the duty slots in exam order, the half and the timetabled papers.</summary>
        private sealed record Group(ExamMaster Exam, List<Guid> CopyIds, List<Slot> Slots, string Half, int BranchCount, List<Paper> Papers);

        private sealed record Paper(Guid ExamId, Guid? CourseId, Guid? SubjectId, string Date, string Time, string Branch, int? Semester);

        private static string SlotKey(string date, string time) => date.Trim().ToLowerInvariant() + "|" + time.Trim().ToLowerInvariant();

        private static string TitleOf(ExamMaster exam) =>
            string.Join(' ', new[] { exam.Name, exam.ExamType }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

        public async Task<List<ExamGroupDto>> ExamsAsync(Guid ayid)
        {
            var exams = await _context.Exams.AsNoTracking()
                .Where(e => e.AcademicYearAYID == ayid && e.RevaluationForExamId == null).ToListAsync();
            var examIds = exams.Select(e => e.ExamId).ToList();
            var papers = await PapersAsync(examIds);
            var lettered = await _context.StaffLetters.AsNoTracking()
                .Where(l => (l.ExamId != null && examIds.Contains(l.ExamId.Value)))
                .Select(l => new { l.ExamId, l.LetterType }).ToListAsync();

            var result = new List<ExamGroupDto>();
            foreach (var copies in exams.GroupBy(e => (Name: e.Name.ToUpperInvariant(), Type: (e.ExamType ?? "").ToUpperInvariant())))
            {
                var ids = copies.Select(e => e.ExamId).ToHashSet();
                var group = Describe(copies.ToList(), papers.Where(p => ids.Contains(p.ExamId)).ToList());
                if (group.Slots.Count == 0) continue;
                result.Add(ToDto(group,
                    lettered.Count(l => l.ExamId != null && ids.Contains(l.ExamId.Value) && l.LetterType == StaffLetterTypes.Supervision),
                    lettered.Count(l => l.ExamId != null && ids.Contains(l.ExamId.Value) && IsAppointment(l.LetterType))));
            }
            return result.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.ExamType, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task<SupervisionDto> SupervisionAsync(Guid examId)
        {
            var group = await GroupAsync(examId);
            var letters = await LettersAsync(group);
            return new SupervisionDto
            {
                Exam = ToDto(group, letters.Count, await AppointmentLettersCountAsync(group)),
                Roles = SupervisionRoles.All.ToList(),
                Slots = group.Slots.Select(s => new SupervisionSlotDto { Date = s.Date, Time = s.Time, Papers = s.Papers, Branches = s.Branches }).ToList(),
                Letters = letters
            };
        }

        /// <summary>The exam's group, looked up from any branch's copy. Revaluation exams are not for letters (the refusal names the kind of letter).</summary>
        private async Task<Group> GroupAsync(Guid examId, string letters = "Supervision letters")
        {
            var copyIds = await ExamGroups.CopyIdsAsync(_context, examId);
            if (copyIds.Count == 0) throw new KeyNotFoundException("Exam not found.");
            var copies = await _context.Exams.AsNoTracking().Where(e => copyIds.Contains(e.ExamId)).ToListAsync();
            if (copies.Any(e => e.RevaluationForExamId != null))
                throw new InvalidOperationException($"{letters} are for regular exams, not revaluation exams.");
            return Describe(copies, await PapersAsync(copyIds));
        }

        private static bool IsAppointment(string type) => type == StaffLetterTypes.Examiner || type == StaffLetterTypes.Moderator;

        /// <summary>The timetabled papers of the given exams (rows with a date and a time), with their branch and semester.</summary>
        private async Task<List<Paper>> PapersAsync(List<Guid> examIds)
        {
            var rows = await _context.TimeTables.AsNoTracking()
                .Where(t => t.ExamId != null && examIds.Contains(t.ExamId.Value) && t.Date != null && t.Date != "" && t.Time != null && t.Time != "")
                .Select(t => new { ExamId = t.ExamId!.Value, t.CourseId, t.SubjectId, Date = t.Date!, Time = t.Time! })
                .ToListAsync();
            var subjectIds = rows.Where(r => r.SubjectId != null).Select(r => r.SubjectId!.Value).Distinct().ToList();
            var courseIds = rows.Where(r => r.CourseId != null).Select(r => r.CourseId!.Value).Distinct().ToList();
            var semesters = (await _context.SubjectMasters.AsNoTracking().Where(s => subjectIds.Contains(s.SubjectId))
                    .Select(s => new { s.SubjectId, s.SemId }).ToListAsync())
                .ToDictionary(s => s.SubjectId, s => SemesterNumber(s.SemId));
            var branches = (await _context.CourseMasters.AsNoTracking().Where(c => courseIds.Contains(c.CourseId))
                    .Select(c => new { c.CourseId, c.Name }).ToListAsync())
                .ToDictionary(c => c.CourseId, c => c.Name);

            return rows.Select(r => new Paper(
                r.ExamId, r.CourseId, r.SubjectId, r.Date.Trim(), r.Time.Trim(),
                r.CourseId != null && branches.TryGetValue(r.CourseId.Value, out var branch) ? branch : "",
                r.SubjectId != null && semesters.TryGetValue(r.SubjectId.Value, out var sem) ? sem : null)).ToList();
        }

        private static int? SemesterNumber(string? semId)
        {
            var digits = new string((semId ?? "").Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var n) ? n : null;
        }

        private static Group Describe(List<ExamMaster> copies, List<Paper> papers)
        {
            var canonical = copies.OrderBy(e => e.CreatedAt).ThenBy(e => e.ExamId).First();

            // Distinct (date, time) pairs in exam order.
            var slots = new List<Slot>();
            foreach (var g in papers.GroupBy(p => SlotKey(p.Date, p.Time)))
            {
                var first = g.OrderBy(p => p.Date, StringComparer.Ordinal).ThenBy(p => p.Time, StringComparer.Ordinal).First();
                var branches = g.Select(p => p.Branch).Where(b => b != "").Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(b => b, StringComparer.OrdinalIgnoreCase).ToList();
                slots.Add(new Slot(first.Date, first.Time, g.Select(p => p.SubjectId?.ToString() ?? p.Branch).Distinct().Count(), branches));
            }
            slots = slots.OrderBy(s => ExamSlotOrder.Date(s.Date)).ThenBy(s => ExamSlotOrder.StartTime(s.Time))
                .ThenBy(s => s.Time, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Date, StringComparer.OrdinalIgnoreCase).ToList();

            // First half = odd semesters only, second half = even only; a mix (or no semester at all) has no half.
            var semesters = papers.Where(p => p.Semester != null).Select(p => p.Semester!.Value).Distinct().ToList();
            var half = semesters.Count == 0 ? "" : semesters.All(s => s % 2 == 1) ? "First Half" : semesters.All(s => s % 2 == 0) ? "Second Half" : "";
            var branchCount = papers.Select(p => p.CourseId).Where(c => c != null).Distinct().Count();
            return new Group(canonical, copies.Select(e => e.ExamId).ToList(), slots, half, branchCount, papers);
        }

        private static ExamGroupDto ToDto(Group group, int letterCount, int appointmentCount) => new()
        {
            ExamId = group.Exam.ExamId,
            Name = group.Exam.Name,
            ExamType = group.Exam.ExamType ?? "",
            Title = TitleOf(group.Exam),
            Half = group.Half,
            BranchCount = group.BranchCount,
            SlotCount = group.Slots.Count,
            LetterCount = letterCount,
            AppointmentCount = appointmentCount
        };

        // ------------------------------------------------------------------ supervision letters

        private async Task<List<SupervisionLetterDto>> LettersAsync(Group group)
        {
            var letters = await _context.StaffLetters.AsNoTracking().Include(l => l.Duties)
                .Where(l => l.LetterType == StaffLetterTypes.Supervision && (l.ExamId != null && group.CopyIds.Contains(l.ExamId.Value))).ToListAsync();
            return letters.OrderBy(l => l.SerialNo).Select(ToDto).ToList();
        }

        private static SupervisionLetterDto ToDto(StaffLetter l) => new()
        {
            LetterId = l.Id,
            StaffId = l.StaffId,
            StaffName = l.StaffName,
            LetterNo = l.LetterNo,
            LetterDate = l.LetterDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Role = l.Role ?? "",
            Duties = l.Duties.OrderBy(d => d.SortOrder).Select(d => new DutyDto { Date = d.Date, Time = d.Time }).ToList()
        };

        /// <summary>
        /// Makes the exam's supervision letters match the grid: people in it get a new letter or an updated one (number
        /// and date kept), letters of people not in it are cancelled. Everything is checked before anything is saved.
        /// </summary>
        public async Task<SupervisionSaveResult> SaveSupervisionAsync(SupervisionSave dto)
        {
            var group = await GroupAsync(dto.ExamId);
            var exam = group.Exam;
            var slots = group.Slots.Select((s, i) => (Key: SlotKey(s.Date, s.Time), Slot: s, Order: i)).ToDictionary(s => s.Key);

            var assignments = dto.Assignments ?? new List<SupervisionAssignment>();
            var staffIds = assignments.Select(a => a.StaffId).Distinct().ToList();
            var staff = await _context.StaffMasters.AsNoTracking().Where(s => staffIds.Contains(s.StaffId)).ToDictionaryAsync(s => s.StaffId);
            var errors = new List<string>();
            var seen = new HashSet<Guid>();
            var plan = new List<(StaffMaster Staff, string Role, List<Slot> Duties)>();
            foreach (var a in assignments)
            {
                if (!staff.TryGetValue(a.StaffId, out var person))
                {
                    errors.Add("a member of staff in the list was not found.");
                    continue;
                }
                var who = StaffService.Letter(person.Title, person.FirstName, person.MiddleName, person.LastName);
                if (!seen.Add(a.StaffId))
                {
                    errors.Add($"{who} appears more than once.");
                    continue;
                }

                var problems = new List<string>();
                if (!person.IsActive) problems.Add("has left and cannot be given duties.");
                else if (person.IsExternal) problems.Add("is an external member of staff; supervision duties are for our own staff.");

                var role = SupervisionRoles.All.FirstOrDefault(r => string.Equals(r, (a.Role ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
                if (role == null) problems.Add($"needs a role ({string.Join(", ", SupervisionRoles.All)}).");

                var duties = new List<(string Key, Slot Slot, int Order)>();
                foreach (var duty in a.Duties ?? new List<DutyDto>())
                {
                    var key = SlotKey(duty.Date ?? "", duty.Time ?? "");
                    if (!slots.TryGetValue(key, out var slot)) problems.Add($"has a duty on {(duty.Date ?? "").Trim()} {(duty.Time ?? "").Trim()}, which is not a date and time of this exam.");
                    else if (duties.All(d => d.Key != key)) duties.Add((key, slot.Slot, slot.Order));
                }
                if (duties.Count == 0 && problems.Count == 0) problems.Add("needs at least one date.");

                errors.AddRange(problems.Select(p => $"{who} {p}"));
                if (problems.Count == 0) plan.Add((person, role!, duties.OrderBy(d => d.Order).Select(d => d.Slot).ToList()));
            }

            DateTime? letterDate = null;
            if (!string.IsNullOrWhiteSpace(dto.LetterDate))
            {
                if (DateTime.TryParseExact(dto.LetterDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) letterDate = parsed.Date;
                else errors.Add("the letter date is not a valid date.");
            }
            if (exam.AcademicYearAYID == null)
                errors.Add("this exam has no academic year.");
            if (errors.Count > 0)
                throw new InvalidOperationException(Sentence(errors));

            var existing = await _context.StaffLetters.Include(l => l.Duties)
                .Where(l => l.LetterType == StaffLetterTypes.Supervision && (l.ExamId != null && group.CopyIds.Contains(l.ExamId.Value))).ToListAsync();
            if (letterDate == null && plan.Any(p => existing.All(l => l.StaffId != p.Staff.StaffId)))
                throw new InvalidOperationException("Enter the letter date.");

            var collegeId = exam.CollegeId ?? _context.CurrentCollegeId;
            var ayid = exam.AcademicYearAYID!.Value;
            var result = new SupervisionSaveResult();

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                string? numberStart = null;
                var nextSerial = 0;
                foreach (var (person, role, duties) in plan)
                {
                    var letter = existing.FirstOrDefault(l => l.StaffId == person.StaffId);
                    if (letter != null)
                    {
                        var same = letter.Role == role && letter.ExamId == exam.ExamId
                            && letter.Duties.OrderBy(d => d.SortOrder).Select(d => SlotKey(d.Date, d.Time)).SequenceEqual(duties.Select(d => SlotKey(d.Date, d.Time)));
                        if (same)
                        {
                            result.Unchanged++;
                            continue;
                        }
                        letter.Role = role;
                        letter.ExamId = exam.ExamId;
                        letter.UpdatedAt = DateTime.UtcNow;
                        _context.StaffLetterDuties.RemoveRange(letter.Duties.ToList());
                        AddDuties(letter, duties, collegeId);
                        result.Updated++;
                        continue;
                    }

                    if (numberStart == null)
                    {
                        numberStart = await NumberStartAsync(collegeId, ayid, StaffLetterTypes.Supervision);
                        nextSerial = await NextSerialAsync(collegeId, ayid, StaffLetterTypes.Supervision);
                    }
                    letter = new StaffLetter
                    {
                        Id = Guid.NewGuid(),
                        CollegeId = collegeId,
                        StaffId = person.StaffId,
                        LetterType = StaffLetterTypes.Supervision,
                        ExamId = exam.ExamId,
                        AYID = ayid,
                        SerialNo = nextSerial,
                        LetterNo = $"{numberStart}/{nextSerial:D4}",
                        LetterDate = letterDate!.Value,
                        Role = role,
                        StaffName = StaffService.Letter(person.Title, person.FirstName, person.MiddleName, person.LastName),
                        ExamTitle = TitleOf(exam),
                        Half = group.Half
                    };
                    nextSerial++;
                    AddDuties(letter, duties, collegeId);
                    _context.StaffLetters.Add(letter);
                    result.Created++;
                }

                var kept = plan.Select(p => p.Staff.StaffId).ToHashSet();
                foreach (var letter in existing.Where(l => !kept.Contains(l.StaffId)))
                {
                    letter.IsDeleted = true;
                    letter.DeletedAt = DateTime.UtcNow;
                    result.Removed++;
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            result.Message = $"Saved: {result.Created} new, {result.Updated} updated, {result.Unchanged} unchanged, {result.Removed} cancelled.";
            result.Letters = await LettersAsync(group);
            return result;
        }

        private void AddDuties(StaffLetter letter, List<Slot> duties, Guid? collegeId)
        {
            for (int i = 0; i < duties.Count; i++)
            {
                var duty = new StaffLetterDuty
                {
                    Id = Guid.NewGuid(),
                    CollegeId = collegeId,
                    LetterId = letter.Id,
                    Date = duties[i].Date,
                    Time = duties[i].Time,
                    SortOrder = i
                };
                _context.StaffLetterDuties.Add(duty);
            }
        }

        // ------------------------------------------------------------------ examiner and moderator letters

        public async Task<AppointmentsDto> AppointmentsAsync(Guid examId)
        {
            var group = await GroupAsync(examId, "Appointment letters");
            var letters = await AppointmentLettersAsync(group);
            var supervision = await _context.StaffLetters.AsNoTracking()
                .CountAsync(l => l.LetterType == StaffLetterTypes.Supervision && (l.ExamId != null && group.CopyIds.Contains(l.ExamId.Value)));
            return new AppointmentsDto
            {
                Exam = ToDto(group, supervision, letters.Count),
                Subjects = await AppointmentSubjectsAsync(group),
                Letters = letters
            };
        }

        /// <summary>The subjects on the exam's timetable (all branches), once each: by branch, semester, then subject order.</summary>
        private async Task<List<AppointmentSubjectDto>> AppointmentSubjectsAsync(Group group)
        {
            var papers = group.Papers.Where(p => p.SubjectId != null).ToList();
            var ids = papers.Select(p => p.SubjectId!.Value).Distinct().ToList();
            var masters = await _context.SubjectMasters.AsNoTracking().Where(s => ids.Contains(s.SubjectId)).ToDictionaryAsync(s => s.SubjectId);

            var rows = new List<(AppointmentSubjectDto Subject, int? SortOrder)>();
            foreach (var g in papers.GroupBy(p => p.SubjectId!.Value))
            {
                if (!masters.TryGetValue(g.Key, out var subject)) continue;
                // The subject's own branch when it is on the timetable, otherwise the first branch that has it.
                var paper = g.OrderBy(p => p.CourseId == subject.CourseId ? 0 : 1)
                    .ThenBy(p => p.Branch, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.CourseId).First();
                rows.Add((new AppointmentSubjectDto
                {
                    SubjectId = subject.SubjectId,
                    Code = subject.SubjectCode,
                    Name = subject.Name,
                    Branch = paper.Branch,
                    CourseId = paper.CourseId,
                    Semester = paper.Semester,
                    Pattern = subject.Pattern ?? ""
                }, subject.SortOrder));
            }

            var order = Comparer<(AppointmentSubjectDto Subject, int? SortOrder)>.Create((a, b) =>
            {
                var byBranch = StringComparer.OrdinalIgnoreCase.Compare(a.Subject.Branch, b.Subject.Branch);
                if (byBranch != 0) return byBranch;
                var bySemester = (a.Subject.Semester ?? int.MaxValue).CompareTo(b.Subject.Semester ?? int.MaxValue);
                if (bySemester != 0) return bySemester;
                return SubjectOrder.Compare(a.SortOrder, a.Subject.Code, a.Subject.Name, b.SortOrder, b.Subject.Code, b.Subject.Name);
            });
            return rows.OrderBy(r => r, order).Select(r => r.Subject).ToList();
        }

        private async Task<List<AppointmentLetterDto>> AppointmentLettersAsync(Group group)
        {
            var letters = await _context.StaffLetters.AsNoTracking()
                .Where(l => (l.LetterType == StaffLetterTypes.Examiner || l.LetterType == StaffLetterTypes.Moderator) && (l.ExamId != null && group.CopyIds.Contains(l.ExamId.Value)))
                .ToListAsync();
            return letters.OrderBy(l => l.LetterType, StringComparer.Ordinal).ThenBy(l => l.SerialNo).Select(ToAppointmentDto).ToList();
        }

        private Task<int> AppointmentLettersCountAsync(Group group) =>
            _context.StaffLetters.AsNoTracking()
                .CountAsync(l => (l.LetterType == StaffLetterTypes.Examiner || l.LetterType == StaffLetterTypes.Moderator) && (l.ExamId != null && group.CopyIds.Contains(l.ExamId.Value)));

        private static AppointmentLetterDto ToAppointmentDto(StaffLetter l) => new()
        {
            LetterId = l.Id,
            StaffId = l.StaffId,
            StaffName = l.StaffName,
            LetterNo = l.LetterNo,
            LetterDate = l.LetterDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Type = l.LetterType,
            SubjectId = l.SubjectId
        };

        /// <summary>
        /// Makes the exam's examiner and moderator letters match the screen: an appointment that already has a letter keeps it
        /// (number and date), a new one gets a new numbered letter, letters missing from the screen are cancelled. Everything
        /// is checked before anything is saved.
        /// </summary>
        public async Task<AppointmentSaveResult> SaveAppointmentsAsync(AppointmentSave dto)
        {
            var group = await GroupAsync(dto.ExamId, "Appointment letters");
            var exam = group.Exam;
            var subjects = (await AppointmentSubjectsAsync(group)).ToDictionary(s => s.SubjectId);

            var requested = dto.Subjects ?? new List<AppointmentSubjectSave>();
            var staffIds = requested.SelectMany(s => (s.Examiners ?? new List<Guid>()).Concat(s.Moderators ?? new List<Guid>())).Distinct().ToList();
            var staff = await _context.StaffMasters.AsNoTracking().Where(s => staffIds.Contains(s.StaffId)).ToDictionaryAsync(s => s.StaffId);

            // Someone who has left keeps a letter they already hold; they just cannot be given a new one.
            var held = (await _context.StaffLetters.AsNoTracking()
                    .Where(l => (l.LetterType == StaffLetterTypes.Examiner || l.LetterType == StaffLetterTypes.Moderator) && (l.ExamId != null && group.CopyIds.Contains(l.ExamId.Value)))
                    .Select(l => new { l.StaffId, l.SubjectId, l.LetterType }).ToListAsync())
                .Select(l => (l.StaffId, l.SubjectId ?? Guid.Empty, l.LetterType)).ToHashSet();

            var errors = new List<string>();
            var seen = new HashSet<(Guid Staff, Guid Subject, string Type)>();
            var plan = new List<(StaffMaster Staff, AppointmentSubjectDto Subject, string Type)>();
            foreach (var entry in requested)
            {
                if (!subjects.TryGetValue(entry.SubjectId, out var subject))
                {
                    errors.Add("a subject in the list is not on this exam's timetable.");
                    continue;
                }
                var lists = new[]
                {
                    (Type: StaffLetterTypes.Examiner, Other: StaffLetterTypes.Moderator, Ids: entry.Examiners ?? new List<Guid>()),
                    (Type: StaffLetterTypes.Moderator, Other: StaffLetterTypes.Examiner, Ids: entry.Moderators ?? new List<Guid>())
                };
                foreach (var (type, other, ids) in lists)
                {
                    foreach (var staffId in ids)
                    {
                        if (!staff.TryGetValue(staffId, out var person))
                        {
                            errors.Add("a member of staff in the list was not found.");
                            continue;
                        }
                        var who = StaffService.Letter(person.Title, person.FirstName, person.MiddleName, person.LastName);
                        var what = $"{subject.Code} {subject.Name}";
                        if (!person.IsActive && !held.Contains((staffId, subject.SubjectId, type)))
                            errors.Add($"{who} has left and cannot be appointed.");
                        else if (seen.Contains((staffId, subject.SubjectId, type)))
                            errors.Add($"{who} is listed more than once as {type.ToLowerInvariant()} for {what}.");
                        else if (seen.Contains((staffId, subject.SubjectId, other)))
                            errors.Add($"{who} cannot be both examiner and moderator for {what}.");
                        else
                        {
                            seen.Add((staffId, subject.SubjectId, type));
                            plan.Add((person, subject, type));
                        }
                    }
                }
            }

            DateTime? letterDate = null;
            if (!string.IsNullOrWhiteSpace(dto.LetterDate))
            {
                if (DateTime.TryParseExact(dto.LetterDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) letterDate = parsed.Date;
                else errors.Add("the letter date is not a valid date.");
            }
            if (exam.AcademicYearAYID == null)
                errors.Add("this exam has no academic year.");
            if (errors.Count > 0)
                throw new InvalidOperationException(Sentence(errors.Distinct().ToList()));

            var existing = await _context.StaffLetters
                .Where(l => (l.LetterType == StaffLetterTypes.Examiner || l.LetterType == StaffLetterTypes.Moderator) && (l.ExamId != null && group.CopyIds.Contains(l.ExamId.Value)))
                .ToListAsync();
            var byKey = new Dictionary<(Guid Staff, Guid Subject, string Type), StaffLetter>();
            foreach (var l in existing.Where(l => l.SubjectId != null).OrderBy(l => l.SerialNo))
                byKey.TryAdd((l.StaffId, l.SubjectId!.Value, l.LetterType), l);
            if (letterDate == null && plan.Any(p => !byKey.ContainsKey((p.Staff.StaffId, p.Subject.SubjectId, p.Type))))
                throw new InvalidOperationException("Enter the letter date.");

            var collegeId = exam.CollegeId ?? _context.CurrentCollegeId;
            var ayid = exam.AcademicYearAYID!.Value;
            var result = new AppointmentSaveResult();

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var numbering = new Dictionary<string, (string Start, int Next)>();
                var kept = new HashSet<Guid>();
                foreach (var (person, subject, type) in plan)
                {
                    if (byKey.TryGetValue((person.StaffId, subject.SubjectId, type), out var letter))
                    {
                        kept.Add(letter.Id);
                        letter.ExamId = exam.ExamId;   // stored against the canonical copy, whichever copy it was made for
                        result.Unchanged++;
                        continue;
                    }

                    if (!numbering.TryGetValue(type, out var number))
                        number = (await NumberStartAsync(collegeId, ayid, type), await NextSerialAsync(collegeId, ayid, type));
                    numbering[type] = (number.Start, number.Next + 1);
                    _context.StaffLetters.Add(new StaffLetter
                    {
                        Id = Guid.NewGuid(),
                        CollegeId = collegeId,
                        StaffId = person.StaffId,
                        LetterType = type,
                        ExamId = exam.ExamId,
                        AYID = ayid,
                        SubjectId = subject.SubjectId,
                        SerialNo = number.Next,
                        LetterNo = $"{number.Start}/{number.Next:D4}",
                        LetterDate = letterDate!.Value,
                        Role = type == StaffLetterTypes.Examiner ? "Examiner" : "Moderator",
                        StaffName = StaffService.Letter(person.Title, person.FirstName, person.MiddleName, person.LastName),
                        ExamTitle = TitleOf(exam),
                        Half = group.Half
                    });
                    result.Created++;
                }

                foreach (var letter in existing.Where(l => !kept.Contains(l.Id)))
                {
                    letter.IsDeleted = true;
                    letter.DeletedAt = DateTime.UtcNow;
                    result.Removed++;
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            result.Message = $"Saved: {result.Created} new, {result.Unchanged} unchanged, {result.Removed} cancelled.";
            result.Letters = await AppointmentLettersAsync(group);
            return result;
        }

        // ------------------------------------------------------------------ numbering

        /// <summary>"{prefix}/{SUP|EXM|MOD}/{year}": the letter number up to the serial, which is added per letter.</summary>
        private async Task<string> NumberStartAsync(Guid? collegeId, Guid ayid, string type)
        {
            var prefix = await PrefixAsync(collegeId);
            var year = await _context.AcademicYears.AsNoTracking().Where(a => a.AYID == ayid)
                .Select(a => a.ShortDuration ?? a.FullDuration).FirstOrDefaultAsync();
            if (string.IsNullOrWhiteSpace(year))
                throw new InvalidOperationException("The exam's academic year was not found.");
            return $"{prefix}/{StaffLetterTypes.Code(type)}/{ShortYear(year)}";
        }

        /// <summary>The next serial of a letter type in a college and academic year. Deleted letters count: a number is never given out twice.</summary>
        private async Task<int> NextSerialAsync(Guid? collegeId, Guid ayid, string type) =>
            (await _context.StaffLetters.IgnoreQueryFilters()
                .Where(l => l.CollegeId == collegeId && l.LetterType == type && l.AYID == ayid)
                .MaxAsync(l => (int?)l.SerialNo) ?? 0) + 1;

        /// <summary>"2026-2027" -> "2026-27"; anything else as it is.</summary>
        public static string ShortYear(string year) => Regex.Replace(year.Trim(), @"^(\d{4})-\d{2}(\d{2})$", "$1-$2");

        private async Task<string> PrefixAsync(Guid? collegeId)
        {
            var custom = await _context.CollegeSettings.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.CollegeId == collegeId && !s.IsDeleted).Select(s => s.LetterPrefix).FirstOrDefaultAsync();
            return !string.IsNullOrWhiteSpace(custom) ? custom.Trim() : await DefaultPrefixAsync(collegeId);
        }

        private async Task<string> DefaultPrefixAsync(Guid? collegeId)
        {
            var code = await _context.Colleges.AsNoTracking().Where(c => c.CollegeId == collegeId).Select(c => c.CollegeCode).FirstOrDefaultAsync();
            return $"{code}/EXAM";
        }

        // ------------------------------------------------------------------ settings

        public async Task<LetterSettingsDto> GetSettingsAsync()
        {
            var settings = await _context.CollegeSettings.AsNoTracking().FirstOrDefaultAsync();
            return new LetterSettingsDto
            {
                LetterPrefix = settings?.LetterPrefix,
                CapContact = settings?.CapContact,
                DefaultPrefix = await DefaultPrefixAsync(_context.CurrentCollegeId)
            };
        }

        public async Task<LetterSettingsDto> SaveSettingsAsync(LetterSettingsSave dto)
        {
            if (_context.CurrentCollegeId == null)
                throw new InvalidOperationException("Letter settings belong to a college: sign in as that college's admin.");
            var prefix = (dto.LetterPrefix ?? "").Trim();
            var contact = (dto.CapContact ?? "").Trim();
            if (prefix.Length > 40) throw new InvalidOperationException("The prefix is longer than 40 characters.");
            if (prefix != "" && !Regex.IsMatch(prefix, @"^[A-Za-z0-9/.\-]+$"))
                throw new InvalidOperationException("The prefix may only have letters, digits, / - and .");
            if (contact.Length > 100) throw new InvalidOperationException("The contact numbers are longer than 100 characters.");

            var settings = await _context.CollegeSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new CollegeSettings { Id = Guid.NewGuid() };
                _context.CollegeSettings.Add(settings);
            }
            settings.LetterPrefix = prefix == "" ? null : prefix;
            settings.CapContact = contact == "" ? null : contact;
            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                Action = "UPDATE",
                TableName = nameof(CollegeSettings),
                RecordId = settings.Id.ToString(),
                PerformedBy = _context.CurrentUserId,
                UserType = "Staff",
                Event = "LetterSettingsChanged",
                Details = $"Letter prefix: {(prefix == "" ? "(default)" : prefix)}; CAP contact: {(contact == "" ? "(none)" : contact)}.",
            });
            await _context.SaveChangesAsync();
            return await GetSettingsAsync();
        }

        private static string Sentence(List<string> errors)
        {
            var text = string.Join(" ", errors);
            return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
        }
    }
}
