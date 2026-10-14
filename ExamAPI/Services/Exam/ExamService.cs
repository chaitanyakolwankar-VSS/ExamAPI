using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Services.Lookup;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using Microsoft.EntityFrameworkCore;
using System;
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

        // One Save creates the exam (and its revaluation twin) for every branch of the college; branches that already have it are skipped.
        public async Task<ApiResponseDto<object>> CreateExamAsync(Exams dto)
        {
            // 🔹 Transaction start
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var branches = await _context.CourseMasters.OrderBy(c => c.Name).Select(c => new { c.CourseId, c.Name }).ToListAsync();
                var haveIt = (await ExistingBranchIds(dto)).ToHashSet();

                var created = 0;
                var skipped = new List<string>();
                foreach (var branch in branches)
                {
                    if (haveIt.Contains(branch.CourseId))
                    {
                        skipped.Add(branch.Name);
                        continue;
                    }
                    var exam = new ExamMaster
                    {
                        // The key is assigned here rather than by the database so the revaluation
                        // twin below can point at it -- reading exam.ExamId before SaveChangesAsync
                        // used to store an empty Guid, leaving every reval exam unlinked.
                        ExamId = Guid.NewGuid(),
                        CourseId = branch.CourseId,
                        Name = dto.Name,
                        ExamType = dto.ExamType,
                        AcademicYearAYID = dto.Ayid,
                        IsActive = false,
                    };
                    _context.Exams.Add(exam);
                    if (dto.RevalExam)
                    {
                        _context.Exams.Add(new ExamMaster
                        {
                            CourseId = branch.CourseId,
                            Name = dto.Name,
                            ExamType = dto.ExamType,
                            AcademicYearAYID = dto.Ayid,
                            RevaluationForExamId = exam.ExamId,
                            IsActive = false,
                        });
                    }
                    created++;
                }

                if (created == 0)
                {
                    return new ApiResponseDto<object>
                    {
                        Success = false,
                        Message = "Exam already exists for every branch."
                    };
                }

                await _context.SaveChangesAsync();

                // 🔹 Commit transaction
                await transaction.CommitAsync();

                var message = $"Exam created for {created} {(created == 1 ? "branch" : "branches")}";
                if (skipped.Count > 0)
                {
                    message += skipped.Count <= 3
                        ? $"; already existed for {string.Join(", ", skipped)}"
                        : $"; already existed for {skipped.Count} other branches";
                }
                return new ApiResponseDto<object>
                {
                    Success = true,
                    Message = message + "."
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
        // Branches (tenant-filtered by the context) that already hold this exam; the revaluation twin does not count.
        private async Task<List<Guid?>> ExistingBranchIds(Exams dto) =>
            await _context.Exams
                .Where(a => a.Name == dto.Name && a.AcademicYearAYID == dto.Ayid && a.ExamType == dto.ExamType && a.RevaluationForExamId == null)
                .Select(a => a.CourseId)
                .Distinct()
                .ToListAsync();

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


        public async Task<ApiResponseDto<object>> SearchExam(Exams dto)
        {
            try
            {
                // "Exists" only when every branch already has it, so Save stays possible while some branches are missing.
                var haveIt = (await ExistingBranchIds(dto)).ToHashSet();
                var branchIds = await _context.CourseMasters.Select(c => c.CourseId).ToListAsync();
                if (branchIds.Count > 0 && branchIds.All(id => haveIt.Contains(id)))
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
                    Message = "Exam not found."
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
