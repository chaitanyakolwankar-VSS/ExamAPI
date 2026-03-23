using ExamAPI.Controllers;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;

namespace ExamAPI.Services.RegularExam
{
    public class RegularExamService : IRegularExamService
    {
        public readonly ApplicationDbContext _context;
        private readonly IGenericRepository _genericRepository;
        public RegularExamService(ApplicationDbContext context, IGenericRepository genericRepository)
        {
            _context = context;
            _genericRepository = genericRepository;
        }

        public async Task<ApiResponseDto<object>> CheckCredits(CheckCredits dto)
        {
            try
            {
                var dbSubjectIds = await _context.SubjectCreditMasters
    .Where(a =>
        a.SubjectId.HasValue &&
        dto.SubjectIds.Contains(a.SubjectId.Value) &&
        a.AYID == dto.Ayid
    )
    .Select(a => a.SubjectId.Value)
    .Distinct()
    .ToListAsync();

                bool allPresent = !dto.SubjectIds.Except(dbSubjectIds).Any();

                if (!allPresent)
                {
                    return new ApiResponseDto<object>
                    {
                        Success = false,
                        Message = "Credits are not defined for one or more selected subjects"
                    };
                }
                return new ApiResponseDto<object>
                {
                    Success = true,
                    Message = "Credits Defined"
                };
            }
            catch (Exception ex)
            {
                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = "Something went wrong!!"
                };
            }
        }

        public async Task<List<RegularExamResponse>> GetExam(GetExam dto)
        {
            try
            {
                var exams = _context.Exams.Where(a => a.IsActive == true && a.ExamType == "Regular" && a.RevaluationForExamId == null && a.CourseId == dto.Courseid && a.AcademicYearAYID == dto.Ayid).Select(a => new RegularExamResponse
                {
                    ExamId = a.ExamId,
                    Examname = a.RevaluationForExamId != null ? a.Name + " (Revaluation)" : a.Name,
                });
                return exams.ToList();
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        public async Task<RegularStudentResponse> GetStudents(RegularExamStudents dto)
        {
            try
            {
                var creditsID = (
       from cm in _context.SubjectCreditMasters
       where cm.SubjectId.HasValue &&
        dto.SubjectId.Contains(cm.SubjectId.Value) /*cm.SubjectId == dto.ExamInfo.SubjectId*/
          && cm.AYID == dto.Ayid.ToString()
       select cm.CreditsId
   );

                var assignedStudents = _context.StudentMarks.Where(smrks => smrks.CreditsId != null &&
        creditsID.Contains(smrks.CreditsId.Value)).Join(_context.MarksMasters, smrks => smrks.MarksId, mm => mm.MarksId, (smrks, mm) => new { smrks, mm }).Join(_context.StudentMasters, a => a.mm.StdMstId, sm => sm.StdMstId, (a, sm) => new { a, sm }).Where(x => x.a.mm.AcademicYearAYID == dto.Ayid && x.a.mm.ExamId == dto.ExamId && x.a.mm.SemesterId == dto.Semester && x.a.mm.Pattern == dto.Pattern && x.a.mm.StdMstId != null).Select(s => new RegularAssignedStudents { StdMstId = s.a.mm.StdMstId.Value, StudentId = s.a.mm.StudentID, StudentName = s.sm.FirstName + ' ' + s.sm.MiddleName + ' ' + s.sm.LastName, Assigned = true }).Distinct().ToList();


                var assignedStudentIds = _context.StudentMarks.Where(sm => sm.CreditsId != null &&
        creditsID.Contains(sm.CreditsId.Value)).Join(_context.MarksMasters, sm => sm.MarksId, mm => mm.MarksId, (sm, mm) => new { sm, mm }).Where(x => x.mm.AcademicYearAYID == dto.Ayid && x.mm.ExamId == dto.ExamId && x.mm.SemesterId == dto.Semester && x.mm.Pattern == dto.Pattern && x.mm.StdMstId != null).Select(a => a.mm.StdMstId.Value);

                var unassignedStudents = _context.StudentEligibilities.Join(_context.StudentMasters, se => se.StdMstId, sm => sm.StdMstId, (se, sm) => new { se, sm }).Where(a => a.se.CourseId == dto.CourseId && a.se.AYID == dto.Ayid && a.se.SemesterId == dto.Semester && a.se.Pattern == dto.Pattern && !assignedStudentIds.Contains(a.sm.StdMstId)).Select(x => new RegularStudents { StdMstId = x.sm.StdMstId, StudentId = x.sm.StudentId, StudentName = x.sm.FirstName + ' ' + x.sm.MiddleName + ' ' + x.sm.LastName, Assigned = false }).ToList();

                return new RegularStudentResponse
                {
                    AssignedStudents = assignedStudents,
                    UnassignedStudents = unassignedStudents
                };
            }
            catch (Exception ex)
            {
                throw new ApplicationException("Error while fetching students", ex);
            }

        }

        public async Task<ApiResponseDto<object>> SaveRegularExamStudents(SaveRegularExamStudentsDto dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {

                var credits = (
       from cm in _context.SubjectCreditMasters
       join c in _context.SubjectCredits
           on cm.CreditsId equals c.CreditsId
       where cm.SubjectId.HasValue &&
        dto.ExamInfo.SubjectId.Contains(cm.SubjectId.Value) /*cm.SubjectId == dto.ExamInfo.SubjectId*/
          && cm.AYID == dto.ExamInfo.Ayid.ToString()
       select new
       {
           Credit = c,
           SubjectId = cm.SubjectId.Value
       }

   );



                foreach (var student in dto.Students.Where(x => x.Assigned))
                {
                    //Insert in MarksMaster
                    var entity = new MarksMaster
                    {
                        MarksId = Guid.NewGuid(),
                        StudentID = student.StudentId,
                        AcademicYearAYID = dto.ExamInfo.Ayid,
                        SemesterId = dto.ExamInfo.Semester,
                        StdMstId = student.StdMstId,
                        ExamId = dto.ExamInfo.ExamId,
                        Pattern = dto.ExamInfo.Pattern,
                    };
                    _context.MarksMasters.Add(entity);
                    foreach (var cm in credits)
                    {
                        var marksentity = new StudentMarks
                        {
                            Id = Guid.NewGuid(),
                            Head = cm.Credit.Head,
                            MarksId = entity.MarksId,
                            //SubjectId = dto.ExamInfo.SubjectId,
                            SubjectId = cm.SubjectId,
                            CreditsId = cm.Credit.CreditsId,
                        };
                        _context.StudentMarks.Add(marksentity);
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return new ApiResponseDto<object>
                {
                    Success = true,
                    Message = "Students saved successfully!!"
                };
            }
            catch
            {

                await transaction.RollbackAsync();
                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = "Failed to save Students!!"
                };
                throw;
            }
        }

        public async Task<ApiResponseDto<object>> UpdateRegularExamStudents(SaveRegularExamStudentsDto dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var credits = await (
       from cm in _context.SubjectCreditMasters
       join c in _context.SubjectCredits
           on cm.CreditsId equals c.CreditsId
       where cm.SubjectId.HasValue &&
        dto.ExamInfo.SubjectId.Contains(cm.SubjectId.Value)
          && cm.AYID == dto.ExamInfo.Ayid.ToString()
       select c.CreditsId
   ).ToListAsync();
                foreach (var student in dto.Students.Where(x => x.Assigned == false))
                {
                    //Search Student from the MarksMaster

                    var marksId1 = _context.MarksMasters.Where(x => x.StdMstId == student.StdMstId && x.ExamId == dto.ExamInfo.ExamId && x.AcademicYearAYID == dto.ExamInfo.Ayid && x.Pattern == dto.ExamInfo.Pattern).Select(a => a.MarksId);


                    var marksId = await _context.MarksMasters.Where(x => x.StdMstId == student.StdMstId && x.ExamId == dto.ExamInfo.ExamId && x.AcademicYearAYID == dto.ExamInfo.Ayid && x.Pattern == dto.ExamInfo.Pattern).Select(a => a.MarksId).FirstOrDefaultAsync();

                    // Delete All entry from the StudentMarks Subject Credit Id wise 
                    if (marksId != Guid.Empty)
                    {
                        await _genericRepository.DeleteRangeAsync<StudentMarks>(
                            x =>
                                x.CreditsId.HasValue &&
                                credits.Contains(x.CreditsId.Value) &&
                                x.MarksId == marksId
                        );
                    }

                    // Delete All entry from the MarksMaster 
                    await _genericRepository.DeleteAsync<MarksMaster>(marksId);



                }
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return new ApiResponseDto<object>
                {
                    Success = true,
                    Message = "Students Updated successfully!!"
                };
            }
            catch
            {
                await transaction.RollbackAsync();
                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = "Failed to Update Students!!"
                };
            }
        }
    }
}
