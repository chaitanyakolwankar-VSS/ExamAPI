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
        Task<LetterSettingsDto> GetSettingsAsync();
        Task<LetterSettingsDto> SaveSettingsAsync(LetterSettingsSave dto);
    }

    /// <summary>
    /// Letters to staff. Supervision letters: the exam office gives each member of staff a duty role and the dates
    /// they supervise for one exam (all branches together); the screen sends the whole grid and the letters follow it.
    /// Every letter gets a number that is never reused, even when the letter is cancelled.
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

        /// <summary>An exam group: the canonical copy, all copies, the duty slots in exam order and the half.</summary>
        private sealed record Group(ExamMaster Exam, List<Guid> CopyIds, List<Slot> Slots, string Half, int BranchCount);

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
                .Where(l => l.LetterType == StaffLetterTypes.Supervision && examIds.Contains(l.ExamId))
                .Select(l => l.ExamId).ToListAsync();

            var result = new List<ExamGroupDto>();
            foreach (var copies in exams.GroupBy(e => (Name: e.Name.ToUpperInvariant(), Type: (e.ExamType ?? "").ToUpperInvariant())))
            {
                var ids = copies.Select(e => e.ExamId).ToHashSet();
                var group = Describe(copies.ToList(), papers.Where(p => ids.Contains(p.ExamId)).ToList());
                if (group.Slots.Count == 0) continue;
                result.Add(ToDto(group, lettered.Count(ids.Contains)));
            }
            return result.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.ExamType, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task<SupervisionDto> SupervisionAsync(Guid examId)
        {
            var group = await GroupAsync(examId);
            var letters = await LettersAsync(group);
            return new SupervisionDto
            {
                Exam = ToDto(group, letters.Count),
                Roles = SupervisionRoles.All.ToList(),
                Slots = group.Slots.Select(s => new SupervisionSlotDto { Date = s.Date, Time = s.Time, Papers = s.Papers, Branches = s.Branches }).ToList(),
                Letters = letters
            };
        }

        /// <summary>The exam's group, looked up from any branch's copy. Revaluation exams are not for supervision.</summary>
        private async Task<Group> GroupAsync(Guid examId)
        {
            var copyIds = await ExamGroups.CopyIdsAsync(_context, examId);
            if (copyIds.Count == 0) throw new KeyNotFoundException("Exam not found.");
            var copies = await _context.Exams.AsNoTracking().Where(e => copyIds.Contains(e.ExamId)).ToListAsync();
            if (copies.Any(e => e.RevaluationForExamId != null))
                throw new InvalidOperationException("Supervision letters are for regular exams, not revaluation exams.");
            return Describe(copies, await PapersAsync(copyIds));
        }

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
            return new Group(canonical, copies.Select(e => e.ExamId).ToList(), slots, half, branchCount);
        }

        private static ExamGroupDto ToDto(Group group, int letterCount) => new()
        {
            ExamId = group.Exam.ExamId,
            Name = group.Exam.Name,
            ExamType = group.Exam.ExamType ?? "",
            Title = TitleOf(group.Exam),
            Half = group.Half,
            BranchCount = group.BranchCount,
            SlotCount = group.Slots.Count,
            LetterCount = letterCount
        };

        // ------------------------------------------------------------------ supervision letters

        private async Task<List<SupervisionLetterDto>> LettersAsync(Group group)
        {
            var letters = await _context.StaffLetters.AsNoTracking().Include(l => l.Duties)
                .Where(l => l.LetterType == StaffLetterTypes.Supervision && group.CopyIds.Contains(l.ExamId)).ToListAsync();
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
                .Where(l => l.LetterType == StaffLetterTypes.Supervision && group.CopyIds.Contains(l.ExamId)).ToListAsync();
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
                        numberStart = await NumberStartAsync(collegeId, ayid);
                        // Deleted letters count: a number is never given out twice.
                        nextSerial = (await _context.StaffLetters.IgnoreQueryFilters()
                            .Where(l => l.CollegeId == collegeId && l.LetterType == StaffLetterTypes.Supervision && l.AYID == ayid)
                            .MaxAsync(l => (int?)l.SerialNo) ?? 0) + 1;
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

        // ------------------------------------------------------------------ numbering

        /// <summary>"{prefix}/SUP/{year}": the letter number up to the serial, which is added per letter.</summary>
        private async Task<string> NumberStartAsync(Guid? collegeId, Guid ayid)
        {
            var prefix = await PrefixAsync(collegeId);
            var year = await _context.AcademicYears.AsNoTracking().Where(a => a.AYID == ayid)
                .Select(a => a.ShortDuration ?? a.FullDuration).FirstOrDefaultAsync();
            if (string.IsNullOrWhiteSpace(year))
                throw new InvalidOperationException("The exam's academic year was not found.");
            return $"{prefix}/{StaffLetterTypes.Code(StaffLetterTypes.Supervision)}/{ShortYear(year)}";
        }

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
