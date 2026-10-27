using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Services.Lookup;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using Microsoft.EntityFrameworkCore;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace ExamAPI.Services.GenerateHallTicket
{
    public class GenerateHallTicketService : IGenerateHallTicketService
    {
        public readonly ApplicationDbContext _context;
        public GenerateHallTicketService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<HallTicketSubjects>> GetHallTicketSubject(HallTicketSubjectsRequest dto)
        {
            try
            {
                var subjects = _context.SubjectMasters
        .Join(_context.SubjectCreditMasters,
            s => s.SubjectId,
            cm => cm.SubjectId,
            (s, cm) => new { s, cm })

        .Join(_context.SubjectCredits,
            x => x.cm.CreditsId,
            c => c.CreditsId,
            (x, c) => new { x.s, x.cm, c })

        // 🔹 LEFT JOIN TimeTables
        .GroupJoin(_context.TimeTables,
            x => x.s.SubjectId,
            tt => tt.SubjectId,
            (x, tt) => new { x, tt })

        .SelectMany(
            x => x.tt
                .Where(t => t.ExamId == dto.ExamId && t.CourseId == dto.CourseId)
                .DefaultIfEmpty(),
            (x, tt) => new { x.x.s, x.x.cm, x.x.c, tt }
        )

        .Where(a =>
            a.s.SemId == dto.Semester &&
            a.s.Pattern == dto.Pattern &&
            a.s.CourseId == dto.CourseId &&
            a.cm.AYID == dto.Ayid &&
            // The end-semester exam head: "ESE" (engineering) or "ESA" (pharmacy, End Semester Assessment).
            (a.c.HeadType.Contains("ESE") || a.c.HeadType.Contains("ESA"))
        )

        .Select(se => new HallTicketSubjects
        {
            SubjectId = se.s.SubjectId,
            SubjectCode = se.s.SubjectCode,
            SubjectName = se.s.Name,
            SortOrder = se.s.SortOrder,

            // ✅ timetable hai to value, nahi to blank
            ExamTime = se.tt != null ? se.tt.Time : "",
            ExamDate = se.tt != null ? se.tt.Date : ""
        })

        .Distinct();

                return subjects.ToList()
                    .InSubjectOrder(s => s.SortOrder, s => s.SubjectCode, s => s.SubjectName).ToList();
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        public async Task<List<StudentHallTicketData>> HallTickectStudentData(StudentHallTicketDataRequest dto)
        {
            try
            {
                var studentsData = new List<StudentHallTicketData>();

                var Query = _context.MarksMasters
                    .Join(_context.StudentMasters, mm => mm.StdMstId, sm => sm.StdMstId, (mm, sm) => new
                    {
                        mm,
                        sm
                    })
                    .Where(x => x.mm.SemesterId == dto.Semester && x.mm.ExamId == dto.ExamId && x.mm.AcademicYearAYID == dto.Ayid && x.mm.Pattern == dto.Pattern);

                // 👉 Apply condition only if StudentId is present
          
                if (!string.IsNullOrWhiteSpace(dto.StudentId))
                {
                    Query = Query.Where(x => x.sm.StudentId == dto.StudentId);
                }
                // 👉 Then Select
                var HallTicketStudents = Query.Select(a => new
                {
                    a.mm.MarksId,
                    a.mm.ExamId,
                    a.sm.FirstName,
                    a.sm.MiddleName,
                    a.sm.LastName,
                    a.sm.MotherName,
                    Center = "",
                    Seat = a.mm.SeatNo,
                    StudentId = a.sm.StudentId,
                    Photo = a.sm.PhotoUrl,
                    Sign = a.sm.SignUrl,
                    Ern = a.sm.StudentPRN,
                    a.sm.AbcId
                }).ToList(); // materialise: the loop below runs more queries, which needs MARS if this is still streaming
                foreach (var Student in HallTicketStudents)
                {
                    var StudentSubjects = _context.StudentMarks
                        .Join(_context.TimeTables, stdm => stdm.SubjectId, tt => tt.SubjectId, (stdm, tt) => new { stdm, tt })
                        .Join(_context.SubjectMasters, x => x.tt.SubjectId, subm => subm.SubjectId, (x, subm) => new { x, subm })
                        .Where(a => a.x.stdm.MarksId == Student.MarksId && a.x.tt.ExamId == Student.ExamId)
                        .Select(s => new StudentsHallTicketSubjects { code = s.subm.SubjectCode, name = s.subm.Name, date = s.x.tt.Date, time = s.x.tt.Time, SortOrder = s.subm.SortOrder })
                        .Distinct();

                    studentsData.Add(new StudentHallTicketData
                    {
                        // Last, first, middle, mother; blank parts are skipped.
                        name = StudentNames.Display(Student.LastName, Student.FirstName, Student.MiddleName, Student.MotherName),
                        centre = Student.Center,
                        seat = Student.Seat,
                        Studentid = Student.StudentId,
                        ern = Student.Ern ?? "",
                        abcId = Student.AbcId ?? "",
                        photo = Student.Photo,
                        sign = Student.Sign,
                        subjects = StudentSubjects.ToList()
                            .InSubjectOrder(s => s.SortOrder, s => s.code, s => s.name).ToList()
                    });
                }
                // Read-only: Declare Result / Release Hall Ticket list exams straight from ExamMaster and create their
                // DeclareResult row on save, so generating a hall ticket no longer writes anything.
                return studentsData;
            }
            catch (Exception)
            {
                throw;
            }
        }

        public async Task<HallTicketCollege> HallTicketCollegeData()
        {
            try
            {
                // Banner preferred; fall back to the logo. Both are stored paths, read by the client through the
                // authorised GET /api/Files endpoint (not a public URL).
                // The tenant filter on Colleges already narrows this to the current college; a college
                // without a logo must still return its name and centre, so no logo predicate here.
                var collegedata = _context.Colleges.FirstOrDefault();
                var result = new HallTicketCollege
                {
                    Logo = collegedata?.LogoBannerUrl ?? collegedata?.LogoUrl ?? "",
                    Center = collegedata?.CollegeCenter ?? "",
                    PrincipalSign = collegedata?.PrincipalSignUrl ?? "",
                    ControllerSign = collegedata?.ControllerSignUrl ?? "",
                    Seal = collegedata?.SealUrl ?? "",
                    CollegeName = ExamAPI.Services.Report.CollegeBranding.DisplayName(collegedata?.Name, collegedata?.CollegeCode)
                };
                return result;
            }
            catch (Exception ex)
            {
                var result = new HallTicketCollege
                {
                    Logo = "",
                    Center = ""
                };
                return result;
            }
        }

        public async Task<TimeTableAllDto?> GetTimeTableAll(Guid examId)
        {
            var exam = await _context.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.ExamId == examId);
            if (exam == null) return null;

            var ayLabel = exam.AcademicYearAYID == null ? "" :
                await _context.AcademicYears.AsNoTracking().Where(a => a.AYID == exam.AcademicYearAYID)
                    .Select(a => a.ShortDuration ?? a.FullDuration).FirstOrDefaultAsync() ?? "";

            // Every branch's copy of this exam (same name, type and academic year; revaluation exams only with revaluation exams).
            var copyIds = await ExamGroups.CopyIdsAsync(_context, exam.ExamId);

            var raw = await (
                from tt in _context.TimeTables.AsNoTracking()
                join s in _context.SubjectMasters.AsNoTracking() on tt.SubjectId equals s.SubjectId
                join c in _context.CourseMasters.AsNoTracking() on tt.CourseId equals c.CourseId
                where tt.ExamId != null && copyIds.Contains(tt.ExamId.Value)
                      && tt.Date != null && tt.Date != "" && tt.Time != null && tt.Time != ""
                select new
                {
                    Branch = c.Name,
                    s.Pattern,
                    s.SemId,
                    s.SubjectCode,
                    SubjectName = s.Name,
                    s.SortOrder,
                    Date = tt.Date!,
                    Time = tt.Time!
                }).ToListAsync();

            static int SemNumber(string? semId)
            {
                var digits = new string((semId ?? "").Where(char.IsDigit).ToArray());
                return int.TryParse(digits, out var n) ? n : int.MaxValue;
            }
            string SemLabel(string? semId)
            {
                var hit = LookupService.Semesters.FirstOrDefault(x => string.Equals(x.Value, semId, StringComparison.OrdinalIgnoreCase))
                          ?? LookupService.Semesters.FirstOrDefault(x => x.Number.ToString() == (semId ?? "").Trim());
                return hit?.Label ?? (semId ?? "");
            }

            var blocks = raw
                .GroupBy(r => new { r.Branch, Pattern = r.Pattern ?? "", SemId = r.SemId ?? "" })
                .OrderBy(g => g.Key.Branch, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => SemNumber(g.Key.SemId))
                .ThenBy(g => g.Key.Pattern, StringComparer.OrdinalIgnoreCase)
                .Select(g => new TimeTableAllBlock
                {
                    BranchName = g.Key.Branch,
                    Pattern = g.Key.Pattern,
                    Semester = SemLabel(g.Key.SemId),
                    Rows = g.InSubjectOrder(r => r.SortOrder, r => r.SubjectCode, r => r.SubjectName)
                        .Select(r => new TimeTableAllRow { Date = r.Date, Time = r.Time, SubjectCode = r.SubjectCode, SubjectName = r.SubjectName })
                        .ToList()
                })
                .ToList();

            return new TimeTableAllDto
            {
                College = await HallTicketCollegeData(),
                ExamName = exam.Name,
                ExamType = exam.ExamType ?? "",
                AcademicYear = ayLabel,
                Blocks = blocks
            };
        }

        public async Task<ApiResponseDto<object>> SaveTimeTable(SaveTimeTable dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                foreach (var timetable in dto.TimeTableData)
                {


                    var exists = await _context.TimeTables.FirstOrDefaultAsync(a => a.ExamId == dto.ExamId && a.CourseId == dto.CourseId && a.SubjectId == timetable.SubjectId);
                    if (exists == null)
                    {
                        if (timetable.ExamTime != "" && timetable.ExamDate != "")
                        {
                            var entries = new TimeTableMaster
                            {
                                Id = Guid.NewGuid(),
                                Time = timetable.ExamTime,
                                Date = timetable.ExamDate,
                                ExamId = dto.ExamId,
                                CourseId = dto.CourseId,
                                SubjectId = timetable.SubjectId
                            };
                            _context.TimeTables.Add(entries);
                        }

                    }
                    else
                    {
                        exists.Time = timetable.ExamTime;
                        exists.Date = timetable.ExamDate;
                        _context.TimeTables.Update(exists);
                    }

                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return new ApiResponseDto<object>
                {
                    Success = true,
                    Message = "Time Table Saved successfully!!"
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = "Data Not Saved!!"
                };
            }
        }
    }
}
