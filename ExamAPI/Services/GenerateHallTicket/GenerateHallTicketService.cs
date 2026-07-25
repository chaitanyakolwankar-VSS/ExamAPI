using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
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

        public async Task<List<RegularExamResponse>> GetExam(GetExam dto)
        {
            try
            {
                var exams = _context.Exams
                    .Where(a => a.IsActive == true && a.RevaluationForExamId == null && a.CourseId == dto.Courseid && a.AcademicYearAYID == dto.Ayid)
                    .Select(a => new RegularExamResponse
                    {
                        ExamId = a.ExamId,
                        Examname = a.RevaluationForExamId != null ? a.Name + " ( " + a.ExamType + " )" + " (Revaluation)" : a.Name + " ( " + a.ExamType + " )",
                    });
                return exams.ToList();
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
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
            a.c.HeadType.Contains("ESE")
        )

        .Select(se => new HallTicketSubjects
        {
            SubjectId = se.s.SubjectId,
            SubjectCode = se.s.SubjectCode,
            SubjectName = se.s.Name,

            // ✅ timetable hai to value, nahi to blank
            ExamTime = se.tt != null ? se.tt.Time : "",
            ExamDate = se.tt != null ? se.tt.Date : ""
        })

        .Distinct();

                return subjects.ToList();
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
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
                    Name = a.sm.FirstName + " " + a.sm.MiddleName + " " + a.sm.LastName,
                    Center = "",
                    Seat = a.mm.SeatNo,
                    StudentId = a.sm.StudentId
                });
                foreach (var Student in HallTicketStudents)
                {
                    var StudentSubjects = _context.StudentMarks
                        .Join(_context.TimeTables, stdm => stdm.SubjectId, tt => tt.SubjectId, (stdm, tt) => new { stdm, tt })
                        .Join(_context.SubjectMasters, x => x.tt.SubjectId, subm => subm.SubjectId, (x, subm) => new { x, subm })
                        .Where(a => a.x.stdm.MarksId == Student.MarksId && a.x.tt.ExamId == Student.ExamId)
                        .Select(s => new StudentsHallTicketSubjects { code = s.subm.SubjectCode, name = s.subm.Name, date = s.x.tt.Date, time = s.x.tt.Time })
                        .Distinct();

                    studentsData.Add(new StudentHallTicketData
                    {
                        name = Student.Name,
                        centre = Student.Center,
                        seat = Student.Seat,
                        Studentid=Student.StudentId,
                        subjects = StudentSubjects.ToList()
                    });
                }

                return studentsData;
            }
            catch (Exception ex)
            {
                throw new NotImplementedException();
            }
        }

        public async Task<HallTicketCollege> HallTicketCollegeData()
        {
            try
            {
                var collegedata = _context.Colleges.FirstOrDefault(a=>a.LogoBannerUrl!=null);
                var result = new HallTicketCollege
                {
                    Logo = collegedata.LogoBannerUrl,
                    Center=collegedata.CollegeCenter
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
