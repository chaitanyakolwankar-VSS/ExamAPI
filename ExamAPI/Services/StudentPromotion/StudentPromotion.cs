using Azure;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.StudentPromotion
{
    public class StudentPromotion : IStudentPromotionService
    {
        private readonly ApplicationDbContext _context;
        private readonly IGenericRepository _genericRepository;
        public StudentPromotion(ApplicationDbContext context, IGenericRepository genericRepository)
        {
            _context = context;
            _genericRepository = genericRepository;
        }

        public async Task<List<SingleStudentData>> GetStudentData(SingleStudentDataRequest dto)
        {
            var student = _context.StudentEligibilities.Join(_context.StudentMasters, m => m.StdMstId, e => e.StdMstId, (m, e) => new { m, e }).Join(_context.CourseMasters, c => c.m.CourseId, cm => cm.CourseId, (c, cm) => new { c, cm }).Join(_context.AcademicYears, a => a.c.m.AYID, ay => ay.AYID, (a, ay) => new { a, ay }).Where(w => w.a.c.m.StudentId == dto.StudentId).Select(s => new SingleStudentData { StudentId = s.a.c.e.StudentId, StudentName = s.a.c.e.FirstName + ' ' + s.a.c.e.MiddleName + ' ' + s.a.c.e.LastName, AcademicYear = s.ay.FullDuration, Branch = s.a.cm.Name, Semester = s.a.c.m.SemesterId == "Sem-1"? "Sem-1,Sem-2" : s.a.c.m.SemesterId == "Sem-3"? "Sem-3,Sem-4": s.a.c.m.SemesterId == "Sem-5"? "Sem-5,Sem-6": s.a.c.m.SemesterId });

            return await student.ToListAsync();
        }
        public async Task<EligibilityStudentResponse> GetAssignedStudent(EligibilityStudentsAssign dto)
        {
            var assignedStudent1 =  _context.StudentEligibilities.Join(_context.StudentMasters, e => e.StdMstId, m => m.StdMstId, (e, m) => new { e, m }).Where(x => x.e.CourseId == dto.CourseId && x.e.Pattern == dto.Pattern && x.e.SemesterId == dto.Semester && x.e.AYID == dto.Ayid).Select(s => new EligibilityAssignedStudent { StdMstId = s.e.StdMstId, StudentId = s.m.StudentId, StudentName = s.m.FirstName + ' ' + s.m.MiddleName + ' ' + s.m.LastName, Eligibility = s.e.IsEligible });
            var assignedStudent = await _context.StudentEligibilities.Join(_context.StudentMasters, e => e.StdMstId, m => m.StdMstId, (e, m) => new {e,m}).Where(x=>x.e.CourseId==dto.CourseId && x.e.Pattern == dto.Pattern && x.e.SemesterId==dto.Semester && x.e.AYID == dto.Ayid ).Select(s=>new EligibilityAssignedStudent { StdMstId=s.e.StdMstId,StudentId=s.m.StudentId,StudentName=s.m.FirstName+' '+s.m.MiddleName+' '+s.m.LastName,Eligibility=s.e.IsEligible }).ToListAsync();

            var excludeStudents = _context.StudentEligibilities.Where(x => x.AYID == dto.Ayid).Select(s => s.StdMstId.Value);
        

            int currentSemester = int.Parse(dto.Semester.Replace("Sem-", ""));

            string PreviousAcademicSemester = currentSemester <= 3
                ? "Sem-1"
                : $"Sem-{currentSemester - 2}";

            var previousSemesters = Enumerable.Range(1, currentSemester - 1)
                                              .Select(i => $"Sem-{i}")
                                              .ToList();

            var UnassignedStudents1 = await _context.StudentEligibilities.Join(_context.StudentMasters, s => s.StdMstId, sm => sm.StdMstId, (s, sm) => new { s, sm }).Join(_context.StudentsOverallResults.Where(r => previousSemesters.Contains(r.SemesterId)), x => x.s.StdMstId, r => r.StdMstId, (x, r) => new { x.s, x.sm, r }).Where(w => w.s.SemesterId == PreviousAcademicSemester && w.s.CourseId == dto.CourseId && w.s.AYID == dto.PreviousAyid && w.s.Pattern==dto.Pattern && w.s.IsEligible==false && !excludeStudents.Contains(w.s.StdMstId.Value)).GroupBy(g => new { g.s.StdMstId, g.sm.StudentId, g.sm.FirstName, g.sm.MiddleName, g.sm.LastName }).Select(g => new EligibilityAssignedStudent { StdMstId = g.Key.StdMstId, StudentId = g.Key.StudentId, StudentName = (g.Key.FirstName ?? "") + " " + (g.Key.MiddleName ?? "") + " " + (g.Key.LastName ?? ""), Eligibility = g.Sum(x => Convert.ToDouble(x.r.CreditGradePoint)) >= (0.5 * g.Sum(x => Convert.ToDouble(x.r.Credits))) ? true : false ,Credits= g.Sum(x => Convert.ToDouble(x.r.Credits)).ToString(),CreditGradePoint = g.Sum(x => Convert.ToDouble(x.r.CreditGradePoint)).ToString() }).ToListAsync();

            var UnassignedStudents = await _context.StudentEligibilities.Join(_context.StudentMasters, s => s.StdMstId, sm => sm.StdMstId, (s, sm) => new { s, sm }).Join(_context.StudentsOverallResults.Where(r => previousSemesters.Contains(r.SemesterId)), x => x.s.StdMstId, r => r.StdMstId, (x, r) => new { x.s, x.sm, r }).Where(w => w.s.SemesterId == PreviousAcademicSemester && w.s.CourseId == dto.CourseId && w.s.AYID == dto.PreviousAyid && w.s.Pattern == dto.Pattern && w.s.IsEligible == false && !excludeStudents.Contains(w.s.StdMstId.Value)).GroupBy(g => new { g.s.StdMstId, g.sm.StudentId, g.sm.FirstName, g.sm.MiddleName, g.sm.LastName }).Select(g => new EligibilityAssignedStudent
            {
                StdMstId = g.Key.StdMstId,

                StudentId = g.Key.StudentId,

                StudentName =
        (g.Key.FirstName ?? "") + " " +
        (g.Key.MiddleName ?? "") + " " +
        (g.Key.LastName ?? ""),

                Eligibility =
        g.Sum(x => Convert.ToDouble(x.r.CreditGradePoint))
        >=
        (0.5 * g.Sum(x => Convert.ToDouble(x.r.Credits))),

                Credits =
        g.Sum(x => Convert.ToDouble(x.r.Credits)).ToString(),

                CreditGradePoint =
        g.Sum(x => Convert.ToDouble(x.r.CreditGradePoint)).ToString(),

                // YAHAN add karo
                SemesterDetails = g
        .GroupBy(x => x.r.SemesterId)
        .Select(sg => new SemesterDetailsDto
        {
            Semester = sg.Key,

            Credit = sg.Sum(x =>
                Convert.ToDecimal(x.r.Credits)),

            CreditGradePoint = sg.Sum(x =>
                Convert.ToDecimal(x.r.CreditGradePoint)),

            CGPI = sg
        .Select(x => Convert.ToDecimal(x.r.CGPI))
        .FirstOrDefault(),

            SGPI = sg
        .Select(x => Convert.ToDecimal(x.r.SGPI))
        .FirstOrDefault()
        })
        .ToList()
            })
.ToListAsync();

            return new EligibilityStudentResponse
            {
                AssignedStudents = assignedStudent,
                UnassignedStudents = UnassignedStudents
            };
        }

        public async Task<ApiResponseDto<object>> SaveEligibility(SaveEligibility dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                Guid? collegeID = await _context.CourseMasters
      .Where(x => x.CourseId == dto.ExamInfo.CourseId)
      .Select(x => x.CollegeId)
      .SingleOrDefaultAsync();
                foreach (var std in dto.Stduents)
                {
                    if (std.IsEligible)
                    {
                        var student = await _context.StudentEligibilities
                        .FirstOrDefaultAsync(x =>
                            x.StdMstId == std.StdMstId &&
                            x.StudentId == std.StudentID &&
                            x.Pattern == dto.ExamInfo.Pattern &&
                            x.CourseId == dto.ExamInfo.CourseId &&
                            x.AYID == dto.ExamInfo.Ayid &&
                            x.SemesterId == dto.ExamInfo.Semester);

                        if (student == null)
                        {
                            _context.StudentEligibilities.Add(new StudentEligibility
                            {
                                Id = Guid.NewGuid(),
                                StdMstId = std.StdMstId,
                                StudentId = std.StudentID,
                                CourseId = dto.ExamInfo.CourseId,
                                AYID = dto.ExamInfo.Ayid,
                                SemesterId = dto.ExamInfo.Semester,
                                CollegeId = collegeID,
                                Pattern = dto.ExamInfo.Pattern
                            });
                        }
                    }
                  
                        
                    
                    
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return new ApiResponseDto<object>
                {
                    Success = true,
                    Message = "Students saved successfully."
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        public async Task<ApiResponseDto<object>> UpdateEligibility(UpdateEligibility dto)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                foreach (var student in dto.Stduents)
                {
                    var existingStudent = await _context.StudentEligibilities
                        .FirstOrDefaultAsync(x =>
                            x.StdMstId == student.StdMstId && x.AYID==dto.ExamInfo.Ayid && x.SemesterId==dto.ExamInfo.Semester);

                    if (existingStudent == null)
                    {
                        continue;
                    }

                    
                        existingStudent.IsEligible = student.Eligibility;
                   
                }

                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return new ApiResponseDto<object>
                {
                    Success = true,
                    Message = "Students Updated successfully."
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

    }

}

