using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using Microsoft.EntityFrameworkCore;
using System.Transactions;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace ExamAPI.Services.Exam
{
    public class ExamService : IExamService
    {
        private readonly IGenericRepository _genericRepository;
        private readonly ApplicationDbContext _context;

        public ExamService(ApplicationDbContext context, IGenericRepository genericRepository)
        {
            _context = context;
            _genericRepository = genericRepository;
        }

        public async Task<ApiResponseDto<object>> CreateExamAsync(Exams dto)
        {
            // 🔹 Transaction start
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var exam_search = _context.Exams.Where(a => a.Name == dto.Name && a.AcademicYearAYID == dto.Ayid && a.ExamType==dto.ExamType);
                if (exam_search.Any())
                {
                    return new ApiResponseDto<object>
                    {
                        Success = false,
                        Message = "Exam already exists. Please use a different Exam."
                    };
                }
                var exam = new ExamMaster
                {
                    CourseId = dto.Courseid,
                    Name = dto.Name,
                    ExamType = dto.ExamType,
                    AcademicYearAYID = dto.Ayid,
                   IsActive=false,
                };

                _context.Exams.Add(exam);
                if (dto.RevalExam)
                {
                    var revalexam = new ExamMaster
                    {
                        CourseId = dto.Courseid,
                        Name = dto.Name,
                        ExamType = dto.ExamType,
                        AcademicYearAYID = dto.Ayid,
                        RevaluationForExamId = exam.ExamId,
                        IsActive = false,
                    };
                    _context.Exams.Add(revalexam);
                }
                await _context.SaveChangesAsync();

                // 🔹 Commit transaction
                await transaction.CommitAsync();

                return new ApiResponseDto<object>
                {
                    Success = true,
                    Message = "Exam saved successfully"
                };
            }
            catch (Exception ex)
            {
                // 🔴 Rollback if anything fails
                await transaction.RollbackAsync();

                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = "Something went wrong while saving the exam."
                };
            }
        }


        public async Task<ApiResponseDto<object>> DeleteExamAsync(DeleteExam dto)
        {
            using var Transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                //Search the exam in the MarksMaster
                var marksexam = _context.MarksMasters.Where(m => m.ExamId == dto.ExamId);

                if (marksexam.Any())
                {
                    return new ApiResponseDto<object>
                    {
                        Success = false,
                        Message = "Marks entry for this exam is already completed, so it can’t be deleted."
                    };
                }

                var examstatus = await _context.Exams.FirstOrDefaultAsync(a => a.ExamId == dto.ExamId);
                if (examstatus != null)
                {
                    await _genericRepository.DeleteAsync<ExamMaster>(examstatus.ExamId);
                }

                //Transaction Commit
                await Transaction.CommitAsync();

                return new ApiResponseDto<object>
                {
                    Success = true,
                    Message = "Exam Deleted successfully"
                };

            }
            catch (Exception ex)
            {
                //Transaction RollBack
                await Transaction.RollbackAsync();

                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = "Exam Not Deleted"
                };
            }
        }

        public async Task<List<GetExamResponse>> GetExam(GetExam dto)
        {
            try
            {
                var exams =  _context.Exams
                    .Where(a => a.CourseId == dto.Courseid &&
                                a.AcademicYearAYID == dto.Ayid &&
                                !a.IsDeleted)
                    .Select(a => new GetExamResponse
                    {
                        ExamId = a.ExamId,
                        Name = a.RevaluationForExamId!=null? a.Name + " (Revaluation)":a.Name,
                        ExamType = a.ExamType,
                        IsActive=a.IsActive,
                    }) ;

                return exams.ToList();
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        public async Task<ApiResponseDto<object>> SearchExam(Exams dto)
        {
            try
            {
                var exam_search = _context.Exams.Where(a => a.Name == dto.Name && a.AcademicYearAYID == dto.Ayid && a.ExamType == dto.ExamType);
                if (exam_search.Any())
                {
                    return new ApiResponseDto<object>
                    {
                        Success = true,
                        Message = "Exam already exists."
                    };
                }
                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = "Something went wrong while Updating the Active Status exam."
                };
            }
            catch
            {
                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = "Something went wrong while Updating the Active Status exam."
                };
            }
        }

        public async Task<ApiResponseDto<object>> UpdateExamAsync(UpdateExam dto)
        {
            //Transaction  Start
            using var transaction=await _context.Database.BeginTransactionAsync();
            try
            {
                var examstatus =await _context.Exams.FirstOrDefaultAsync(a => a.ExamId == dto.ExamId);
                if (examstatus != null)
                {
                    examstatus.IsActive = dto.ActiveStatus;
                }
                _context.Exams.Update(examstatus);
                _context.SaveChanges();

                //Transaction Commit
                await transaction.CommitAsync();
                if (dto.ActiveStatus==true)
                {
                    return new ApiResponseDto<object>
                    {
                        Success = true,
                        Message = "This exam is activated"
                    };
                }
                else
                {
                    return new ApiResponseDto<object>
                    {
                        Success = true,
                        Message = "This exam is Deactivated"
                    };
                }

                   
            }
            catch (Exception ex)
            {
                //Transaction RollBack
                await transaction.RollbackAsync();

                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = "Something went wrong while Updating the Active Status exam."
                };
            }
        }
    }

}
