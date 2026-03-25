using Azure;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Transactions;

namespace ExamAPI.Services.Eligibility
{
    public class EligibilityService:IEligibilityService
    {
        public readonly ApplicationDbContext _context;
        public EligibilityService(ApplicationDbContext context)
        {
            _context = context;
        }

        public Task<List<EligibilityStudents>> EligibilityStudents(GetEligibilityStudents Dto)
        {
            try
            {

                var students = _context.MarksMasters
                    .Join(_context.Exams, mm => mm.ExamId, e => e.ExamId, (mm, e) => new { mm, e })
                    .Join(_context.StudentMasters, x => x.mm.StdMstId, s => s.StdMstId, (x, s) => new { x, s })
                    .Where(w => w.x.mm.AcademicYearAYID == Dto.Ayid && w.x.mm.SemesterId == Dto.Semester && w.x.e.CourseId == Dto.CourseId)
                    .Select(sel => new
                    {
                        StdMstId=sel.x.mm.StdMstId,
                        StudentId= sel.x.mm.StudentID,
                        StudentName=sel.s.FirstName+" "+sel.s.MiddleName+" "+sel.s.LastName
                    });
                var StudentList = new List<EligibilityStudents>();
                foreach (var student in students)
                {
                    var studentObj = new EligibilityStudents
                    {
                        SerialNo = "",
                        StudentId = student.StudentId,
                        StudentName = student.StudentName,
                        semesters = new Dictionary<int, SemesterData>()
                    };

                    // 👉 loop for semesters (1–8 ya jo chahiye)
                    for (int sem = 1; sem <= 8; sem++)
                    {
                        string semester = "sem" + sem;
                        var semData = _context.StudentsOverallResults
                            .Where(o => o.StdMstId == student.StdMstId
                                     && o.SemesterId == semester)
                            .Select(s => new SemesterData
                            {
                                CG =s.CreditGradePoint ?? "0",
                                Credit = s.Credits ?? "0",
                                KT_Theory = s.KtTheory ?? "0",
                                KT_Others = s.KtOther ?? "0"
                            })
                            .FirstOrDefault();

                        if (semData != null)
                        {
                            studentObj.semesters[sem] = semData;
                        }
                    }

                    StudentList.Add(studentObj);
                }

                return Task.FromResult(StudentList);
            }
            catch (Exception ex)
            {
                throw; // ya logging kar
            }
        }

        public async Task<ApiResponseDto<object>> SaveEligibility(List<EligibilityStudents> Dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                foreach (var student in Dto)
                {
                    // 👉 Student mapping (StudentId → StdMstId)
                    var std = await _context.StudentMasters
                        .FirstOrDefaultAsync(x => x.StudentId == student.StudentId);

                    if (std == null)
                        continue;

                    foreach (var sem in student.semesters)
                    {
                        string semId = "sem"+sem.Key;
                        var data = sem.Value;

                        // 👉 check already exist (duplicate avoid)
                        var existing = await _context.StudentsOverallResults
                            .FirstOrDefaultAsync(x =>
                                x.StdMstId == std.StdMstId &&
                                x.SemesterId == semId);

                        if (existing != null)
                        {
                            // 🔄 UPDATE
                            existing.CreditGradePoint = data.CG;
                            existing.Credits = data.Credit;
                            existing.KtTheory = data.KT_Theory;
                            existing.KtOther = data.KT_Others;
                            existing.UpdatedAt = DateTime.Now;
                        }
                        else
                        {
                            // ➕ INSERT
                            var entity = new StudentsOverallResult
                            {
                                Id = Guid.NewGuid(),
                                SemesterId = semId,
                                CreditGradePoint = data.CG,
                                Credits = data.Credit,
                                KtTheory = data.KT_Theory,
                                KtOther = data.KT_Others,
                                StdMstId = std.StdMstId,
                                CreatedAt = DateTime.Now,
                                IsDeleted = false
                            };

                            await _context.StudentsOverallResults.AddAsync(entity);
                        }
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync(); // ✅ IMPORTANT

                return new ApiResponseDto<object>
                {
                    Success = true,
                    Message = "Eligibility saved successfully"
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(); // ❗ rollback

                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }
    }
}
