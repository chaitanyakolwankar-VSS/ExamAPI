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
        Task<AppointmentsDto> AppointmentsAsync(Guid examId);
        Task<AppointmentSaveResult> SaveAppointmentsAsync(AppointmentSave dto);
        Task<LetterSettingsDto> GetSettingsAsync();
        Task<LetterSettingsDto> SaveSettingsAsync(LetterSettingsSave dto);
    }

    /// <summary>
    /// Letters to staff. Appointment letters: the exam office appoints an examiner and a moderator for each subject of
    /// an exam (all branches together), and the screen sends all of them at once. Supervision letters come from duty
    /// charts (see <see cref="DutyChartService"/>). Every letter gets a number that is never reused, even when the
    /// letter is cancelled; <see cref="LetterNumbering"/> hands the numbers out.
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
                result.Add(ToDto(group, lettered.Count(l => l.ExamId != null && ids.Contains(l.ExamId.Value) && IsAppointment(l.LetterType))));
            }
            return result.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.ExamType, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The exam's group, looked up from any branch's copy. Revaluation exams are not for letters (the refusal names the kind of letter).</summary>
        private async Task<Group> GroupAsync(Guid examId, string letters = "Appointment letters")
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

        private static ExamGroupDto ToDto(Group group, int appointmentCount) => new()
        {
            ExamId = group.Exam.ExamId,
            Name = group.Exam.Name,
            ExamType = group.Exam.ExamType ?? "",
            Title = TitleOf(group.Exam),
            Half = group.Half,
            BranchCount = group.BranchCount,
            SlotCount = group.Slots.Count,
            LetterCount = 0,
            AppointmentCount = appointmentCount
        };

        // ------------------------------------------------------------------ examiner and moderator letters

        public async Task<AppointmentsDto> AppointmentsAsync(Guid examId)
        {
            var group = await GroupAsync(examId);
            var letters = await AppointmentLettersAsync(group);
            return new AppointmentsDto
            {
                Exam = ToDto(group, letters.Count),
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
            var group = await GroupAsync(dto.ExamId);
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
                var numbering = new Dictionary<string, LetterNumbering>();
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

                    if (!numbering.TryGetValue(type, out var numbers))
                        numbering[type] = numbers = await LetterNumbering.OpenAsync(_context, collegeId, ayid, type);
                    var (serial, letterNo) = numbers.Take();
                    _context.StaffLetters.Add(new StaffLetter
                    {
                        Id = Guid.NewGuid(),
                        CollegeId = collegeId,
                        StaffId = person.StaffId,
                        LetterType = type,
                        ExamId = exam.ExamId,
                        AYID = ayid,
                        SubjectId = subject.SubjectId,
                        SerialNo = serial,
                        LetterNo = letterNo,
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

        // ------------------------------------------------------------------ settings

        public async Task<LetterSettingsDto> GetSettingsAsync()
        {
            var settings = await _context.CollegeSettings.AsNoTracking().FirstOrDefaultAsync();
            return new LetterSettingsDto
            {
                LetterPrefix = settings?.LetterPrefix,
                CapContact = settings?.CapContact,
                ReportingPlace = settings?.ReportingPlace,
                DutyGuidelines = settings?.DutyGuidelines,
                DefaultPrefix = await LetterNumbering.DefaultPrefixAsync(_context, _context.CurrentCollegeId),
                DefaultReportingPlace = DutyLetterDefaults.ReportingPlace,
                DefaultGuidelines = DutyLetterDefaults.Guidelines.ToList()
            };
        }

        public async Task<LetterSettingsDto> SaveSettingsAsync(LetterSettingsSave dto)
        {
            if (_context.CurrentCollegeId == null)
                throw new InvalidOperationException("Letter settings belong to a college: sign in as that college's admin.");
            var prefix = (dto.LetterPrefix ?? "").Trim();
            var contact = (dto.CapContact ?? "").Trim();
            var place = (dto.ReportingPlace ?? "").Trim();
            var lines = (dto.DutyGuidelines ?? "").Split('\n').Select(l => l.Trim()).Where(l => l != "").ToList();
            var guidelines = string.Join("\n", lines);
            if (prefix.Length > 40) throw new InvalidOperationException("The prefix is longer than 40 characters.");
            if (prefix != "" && !Regex.IsMatch(prefix, @"^[A-Za-z0-9/.\-]+$"))
                throw new InvalidOperationException("The prefix may only have letters, digits, / - and .");
            if (contact.Length > 100) throw new InvalidOperationException("The contact numbers are longer than 100 characters.");
            if (place.Length > 200) throw new InvalidOperationException("The reporting place is longer than 200 characters.");
            if (lines.Count > 10) throw new InvalidOperationException("There are more than 10 guidelines.");
            if (lines.Any(l => l.Length > 300)) throw new InvalidOperationException("A guideline is longer than 300 characters.");
            if (guidelines.Length > 2000) throw new InvalidOperationException("The guidelines are longer than 2000 characters in all.");

            var settings = await _context.CollegeSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new CollegeSettings { Id = Guid.NewGuid() };
                _context.CollegeSettings.Add(settings);
            }
            settings.LetterPrefix = prefix == "" ? null : prefix;
            settings.CapContact = contact == "" ? null : contact;
            settings.ReportingPlace = place == "" ? null : place;
            settings.DutyGuidelines = guidelines == "" ? null : guidelines;
            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                Action = "UPDATE",
                TableName = nameof(CollegeSettings),
                RecordId = settings.Id.ToString(),
                PerformedBy = _context.CurrentUserId,
                UserType = "Staff",
                Event = "LetterSettingsChanged",
                Details = $"Letter prefix: {(prefix == "" ? "(default)" : prefix)}; CAP contact: {(contact == "" ? "(none)" : contact)}; reporting place: {(place == "" ? "(default)" : place)}; guidelines: {(lines.Count == 0 ? "(default)" : lines.Count + " line(s)")}.",
            });
            await _context.SaveChangesAsync();
            return await GetSettingsAsync();
        }

        /// <summary>"2026-2027" -> "2026-27"; anything else as it is.</summary>
        public static string ShortYear(string year) => Regex.Replace(year.Trim(), @"^(\d{4})-\d{2}(\d{2})$", "$1-$2");

        internal static string Sentence(List<string> errors)
        {
            var text = string.Join(" ", errors);
            return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
        }
    }

    /// <summary>What the duty letters print when the college has not set its own reporting place and guidelines.</summary>
    public static class DutyLetterDefaults
    {
        public const string ReportingPlace = "the Examination Control Room";

        public static readonly IReadOnlyList<string> Guidelines = new[]
        {
            "Follow the University's examination conduct rules and regulations.",
            "If you cannot attend because of an emergency, get written permission from the Principal or the Chief Conductor in advance and arrange a substitute.",
            "Check the answer-book counts and keep the supervision diary up to date throughout the session."
        };

        /// <summary>The college's guidelines (one per line), or the defaults when it has none.</summary>
        public static List<string> GuidelinesOf(string? saved)
        {
            var lines = (saved ?? "").Split('\n').Select(l => l.Trim()).Where(l => l != "").ToList();
            return lines.Count > 0 ? lines : Guidelines.ToList();
        }
    }

    /// <summary>
    /// Hands out letter numbers of one type in a college and academic year: "{prefix}/{year}/{CODE}-{serial:D3}", e.g.
    /// "VIVA/VIT/EXAM/2025-26/DUTY-084". The prefix is the college's own or "{college code}/EXAM". Deleted letters count
    /// when finding the next serial: a number is never given out twice.
    /// </summary>
    public sealed class LetterNumbering
    {
        private readonly string _start;
        private readonly string _code;
        private int _next;

        private LetterNumbering(string start, string code, int next)
        {
            _start = start;
            _code = code;
            _next = next;
        }

        public static async Task<LetterNumbering> OpenAsync(ApplicationDbContext context, Guid? collegeId, Guid ayid, string type)
        {
            var custom = await context.CollegeSettings.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.CollegeId == collegeId && !s.IsDeleted).Select(s => s.LetterPrefix).FirstOrDefaultAsync();
            var prefix = !string.IsNullOrWhiteSpace(custom) ? custom.Trim() : await DefaultPrefixAsync(context, collegeId);
            var year = await context.AcademicYears.AsNoTracking().Where(a => a.AYID == ayid)
                .Select(a => a.ShortDuration ?? a.FullDuration).FirstOrDefaultAsync();
            if (string.IsNullOrWhiteSpace(year))
                throw new InvalidOperationException("The academic year was not found.");
            var last = await context.StaffLetters.IgnoreQueryFilters()
                .Where(l => l.CollegeId == collegeId && l.LetterType == type && l.AYID == ayid)
                .MaxAsync(l => (int?)l.SerialNo) ?? 0;
            return new LetterNumbering($"{prefix}/{StaffLetterService.ShortYear(year)}", StaffLetterTypes.Code(type), last + 1);
        }

        /// <summary>"{college code}/EXAM": the start of the number when the college has not set its own prefix.</summary>
        public static async Task<string> DefaultPrefixAsync(ApplicationDbContext context, Guid? collegeId)
        {
            var code = await context.Colleges.AsNoTracking().Where(c => c.CollegeId == collegeId).Select(c => c.CollegeCode).FirstOrDefaultAsync();
            return $"{code}/EXAM";
        }

        /// <summary>The next serial and its letter number.</summary>
        public (int Serial, string Number) Take()
        {
            var serial = _next++;
            return (serial, $"{_start}/{_code}-{serial:D3}");
        }
    }
}
