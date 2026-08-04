using ExamAPI.Data;
using ExamAPI.DTOs;
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

        public async Task<List<GetCreditHeadResolutionres>> GetCreditHeadResolution(GetCreditHeadResolutionReq dto)
        {

            // ✅ Step 1 — get credits list
            var creditsIds = await _context.SubjectCreditMasters
                .Where(x => x.SubjectId == dto.SubjectId && x.AYID == dto.Ayid)
                .Select(x => x.CreditsId)
                .ToListAsync();

            // ✅ Step 2 — if empty return empty list
            if (creditsIds == null || !creditsIds.Any())
                return new List<GetCreditHeadResolutionres>();

            // ✅ Step 3 — query subject credits
            var result = await _context.SubjectCredits
                .Where(x => x.CreditsId.HasValue && creditsIds.Contains(x.CreditsId.Value))
                .GroupBy(x => x.CreditsId)
                .Select(g => new GetCreditHeadResolutionres
                {
                    CreditsId = g.Key.Value,

                    H1SubjectCredit = g.Where(x => x.Head == "H1").Select(x => x.Id).FirstOrDefault(),
                    H1OutOf = g.Where(x => x.Head == "H1").Select(x => x.HeadOutOf).FirstOrDefault(),
                    H1Pass = g.Where(x => x.Head == "H1").Select(x => x.HeadPass).FirstOrDefault(),
                    H1Type = g.Where(x => x.Head == "H1").Select(x => x.HeadType).FirstOrDefault(),
                    H1Res = _context.Resolution
            .Where(r => r.CreditID == g.Key && r.AYID == Guid.Parse(dto.Ayid) && r.ExamID==dto.ExamId && r.Head == "H1")
            .Select(r => r.Resolution)
            .FirstOrDefault() ?? "",

                    H2SubjectCredit = g.Where(x => x.Head == "H2").Select(x => x.Id).FirstOrDefault(),
                    H2OutOf = g.Where(x => x.Head == "H2").Select(x => x.HeadOutOf).FirstOrDefault(),
                    H2Pass = g.Where(x => x.Head == "H2").Select(x => x.HeadPass).FirstOrDefault(),
                    H2Type = g.Where(x => x.Head == "H2").Select(x => x.HeadType).FirstOrDefault(),
                    H2Res = _context.Resolution
            .Where(r => r.CreditID == g.Key  && r.AYID == Guid.Parse(dto.Ayid) && r.ExamID == dto.ExamId && r.Head == "H2")
            .Select(r => r.Resolution)
            .FirstOrDefault() ?? "",
                })
                .ToListAsync();

            return result;
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

        public async Task<List<ResolutionExamResponse>> GetResolutionExam(GetResolutionExam dto)
        {
            try
            {
                var examcodes = _context.MarksMasters.Where(a => a.SemesterId == dto.Semester && a.Pattern==dto.Pattern).Select(x => x.ExamId).Distinct();
                var exams = _context.Exams.Where(a =>  a.CourseId == dto.Courseid && a.AcademicYearAYID == dto.Ayid && examcodes.Contains(a.ExamId)).Select(a => new ResolutionExamResponse
                {
                    ExamId = a.ExamId,
                    Examname = a.RevaluationForExamId != null ? a.Name+' '+a.ExamType + " (Revaluation)" : a.Name + ' ' + a.ExamType,
                });
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

        public async Task<ApiResponseDto<object>> SaveCreditHeadResolutionres(SaveCreditHeadResolutionres dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                foreach (var item in dto.Items)
                {
                    if (item.H1SubjectCredit != null)
                    {
                        var H1Reso = new ResolutionMaster()
                        {
                            ID = Guid.NewGuid(),
                            ExamID = dto.ExamId,
                            CreditID = item.CreditsId,
                            SubjectCreditID = item.H1SubjectCredit,
                            Head = "H1",
                            Resolution = string.IsNullOrWhiteSpace(item.H1Res) ? "0" : item.H1Res,
                            CourseID = dto.CourseId,
                            AYID = dto.Ayid,
                            CreatedAt=DateTime.Now
                        };
                        _context.Resolution.Add(H1Reso);
                    }

                    if (item.H2SubjectCredit.HasValue && item.H2SubjectCredit.Value != Guid.Empty)
                    {
                        var H1Reso = new ResolutionMaster()
                        {
                            ID = Guid.NewGuid(),
                            ExamID = dto.ExamId,
                            CreditID = item.CreditsId,
                            SubjectCreditID = item.H2SubjectCredit.Value,
                            Head = "H2",
                            Resolution = string.IsNullOrWhiteSpace(item.H2Res) ? "0" : item.H2Res,
                            CourseID = dto.CourseId,
                            AYID = dto.Ayid,
                            CreatedAt = DateTime.Now
                        };
                        _context.Resolution.Add(H1Reso);
                    }
                }
                _context.SaveChanges();
                await transaction.CommitAsync();
                return new ApiResponseDto<object>
                {
                    Success = true,
                    Message = "Credit Resolution Saved Successfully!!"
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = "Credit Resolution Not Saved Successfully!!"
                };
            }
        }

        public async Task<ApiResponseDto<object>> UpdateCreditHeadResolutionres(SaveCreditHeadResolutionres dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                foreach (var item in dto.Items)
                {
                    if (item.H1SubjectCredit != null)
                    {
                        var h1reso = await _context.Resolution.Where(x => x.ExamID == dto.ExamId && x.SubjectCreditID == item.H1SubjectCredit && x.AYID == dto.Ayid).FirstOrDefaultAsync();
                        if (h1reso != null)
                        {
                            h1reso.Resolution = string.IsNullOrWhiteSpace(item.H1Res) ? "0" : item.H1Res;
                        }
                        _context.Resolution.Update(h1reso);
                    }
                    if (item.H2SubjectCredit.HasValue && item.H2SubjectCredit.Value != Guid.Empty)
                    {
                        var h2reso = await _context.Resolution.Where(x => x.ExamID == dto.ExamId && x.SubjectCreditID == item.H2SubjectCredit && x.AYID == dto.Ayid).FirstOrDefaultAsync();
                        if (h2reso != null)
                        {
                            h2reso.Resolution = string.IsNullOrWhiteSpace(item.H2Res) ? "0" : item.H2Res;
                        }
                        _context.Resolution.Update(h2reso);
                    }
                }
                _context.SaveChanges();
                await transaction.CommitAsync();
                return new ApiResponseDto<object>
                {
                    Success = true,
                    Message = "Credit Resolution Updated Successfully!!"
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = "Credit Resolution Not Updated !!"
                };
            }
        }
    }

}
