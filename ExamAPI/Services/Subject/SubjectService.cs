using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace ExamAPI.Services.Subject
{
    public class SubjectService : ISubjectService
    {
        private readonly ApplicationDbContext _context;
        private readonly IGenericRepository _genericRepository;
        public SubjectService(ApplicationDbContext context, IGenericRepository genericRepository)
        {
            _context = context;
            _genericRepository = genericRepository;
        }
        public async Task<List<SubjectDtos>> GetSubjectsAsync(GetSubjectReqDtos dto)
        {

            var subjects = await _context.SubjectMasters.Where(a => a.CourseId == dto.CourseId && a.Pattern == dto.Pattern && a.SemId == dto.Semester).Select(s => new SubjectDtos
            {
                SubjectId = s.SubjectId,
                SubjectName = s.Name
            }).ToListAsync();
            return subjects;
        }
        public async Task<ApiResponseDto<object>> CreateSubjectAsync(CreateSubjectDto dto)
        {
            // 🔹 Transaction start
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var subject = new SubjectMaster
                {
                    SubjectId = Guid.NewGuid(),
                    SubjectCode = dto.SubjectCode,
                    Name = dto.SubjectName,
                    SemId = dto.SemId,
                    SemName = dto.SemName,
                    Pattern = dto.Pattern,
                    CourseId = dto.CourseID
                };

                _context.SubjectMasters.Add(subject);
                await _context.SaveChangesAsync();

                // 🔹 Commit transaction
                await transaction.CommitAsync();

                return new ApiResponseDto<object>
                {
                    Success = true,
                    Message = "Subject saved successfully",
                    Data = null
                };
            }
            catch (Exception ex)
            {
                // 🔴 Rollback if anything fails
                await transaction.RollbackAsync();

                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = "Error while saving subject",
                    Data = ex.Message
                };
            }
        }
        public async Task<ApiResponseDto<object>> SaveCreditAsync(SaveCreditsDto dto)
        {
            // 🔹 Transaction start
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                if (dto.Credits.Count > 0 || dto.Credits.Count==0)
                {
                    if (dto.Credits.Count == 0)
                    {
                        return new ApiResponseDto<object>
                        {
                            Success = false,
                            Message = "Error while saving subject: Credits are required.",
                            Data = null
                        };
                    }
                    if (dto.Credits[0].ExamType.Count==0 && dto.Credits[0].InternalType.Count == 0)
                    {
                        return new ApiResponseDto<object>
                        {
                            Success = false,
                            Message = "Error while saving subject: ExamType or InternalType is required.",
                            Data = null
                        };
                    }
                }
                foreach (var credit in dto.Credits)
                {
                    var subjectCredit = new SubjectCreditMaster
                    {
                        CreditsId = credit.CreditId ?? Guid.NewGuid(),
                        TotalCredits = credit.CreditNo,
                        SubjectId = dto.SubjectId,
                        AYID = dto.Ayid
                    };
                    if (credit.ExamType.Any())
                    {
                        var CreditsH1 = new SubjectCredits
                        {
                            Id = Guid.NewGuid(),
                            CreditsId = subjectCredit.CreditsId,
                            Head = "H1",
                            HeadType = string.Join(" ", credit.ExamType),
                            HeadOutOf = credit.ExamOutOf,
                            HeadPass = credit.ExamPassing,
                            HeadFormula = credit.PassingPercentage,
                        };
                        _context.SubjectCredits.Add(CreditsH1);
                    }
                    if (credit.InternalType.Any())
                    {
                        var CreditsH2 = new SubjectCredits
                        {
                            Id = Guid.NewGuid(),
                            CreditsId = subjectCredit.CreditsId,
                            Head = "H2",
                            HeadType = string.Join(" ", credit.InternalType),
                            HeadOutOf = credit.InternalOutOf,
                            HeadPass = credit.InternalPassing,
                            HeadFormula = credit.PassingPercentage,
                        };
                        _context.SubjectCredits.Add(CreditsH2);
                    }
                    _context.SubjectCreditMasters.Add(subjectCredit);
                }


                _context.SaveChanges();


                // 🔹 Commit transaction
                await transaction.CommitAsync();

                return new ApiResponseDto<object>
                {
                    Success = true,
                    Message = "Subject saved successfully",
                    Data = null
                };
            }
            catch (Exception ex)
            {
                // 🔴 Rollback if anything fails
                await transaction.RollbackAsync();

                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = "Error while saving subject",
                    Data = ex.Message
                };
            }
        }
        public async Task<List<CreditDto>> GetSubjectCreditAsync(GetCredits dto)
        {

            var rawData = await (
         from sc in _context.SubjectCredits
         join cm in _context.SubjectCreditMasters
             on sc.CreditsId equals cm.CreditsId
         join sm in _context.SubjectMasters
             on cm.SubjectId equals sm.SubjectId
         where cm.AYID == dto.Ayid
               && sm.SubjectId == dto.SubjectId
         group new { sc, cm } by new
         {
             sc.CreditsId,
             cm.TotalCredits,
             sc.HeadFormula
         }
         into g
         select new
         {
             CreditId = g.Key.CreditsId,
             CreditNo = g.Key.TotalCredits,

             ExamTypeRaw = g
                 .Where(x => x.sc.HeadType == "TH")
                 .Select(x => x.sc.HeadType)
                 .FirstOrDefault(),

             ExamOutOf = g
                 .Where(x => x.sc.HeadType == "TH")
                 .Select(x => x.sc.HeadOutOf)
                 .FirstOrDefault(),

             ExamPassing = g
                 .Where(x => x.sc.HeadType == "TH")
                 .Select(x => x.sc.HeadPass)
                 .FirstOrDefault(),

             InternalTypeRaw = g
                 .Where(x => x.sc.HeadType != "TH")
                 .Select(x => x.sc.HeadType)
                 .FirstOrDefault(),

             InternalOutOf = g
                 .Where(x => x.sc.HeadType != "TH")
                 .Select(x => x.sc.HeadOutOf)
                 .FirstOrDefault(),

             InternalPassing = g
                 .Where(x => x.sc.HeadType != "TH")
                 .Select(x => x.sc.HeadPass)
                 .FirstOrDefault(),

             PassingPercentage = g.Key.HeadFormula
         }
     ).ToListAsync();
            var result = rawData.Select(x => new CreditDto
            {
                CreditId = x.CreditId,
                CreditNo = x.CreditNo,

                ExamType = string.IsNullOrWhiteSpace(x.ExamTypeRaw)
        ? new List<string>()
        : x.ExamTypeRaw.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList(),

                ExamOutOf = x.ExamOutOf,
                ExamPassing = x.ExamPassing,

                InternalType = string.IsNullOrWhiteSpace(x.InternalTypeRaw)
        ? new List<string>()
        : x.InternalTypeRaw.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList(),

                InternalOutOf = x.InternalOutOf,
                InternalPassing = x.InternalPassing,

                PassingPercentage = x.PassingPercentage
            }).ToList();


            return result.ToList();
        }
        public async Task<ApiResponseDto<object>> UpdateCreditAsync(SaveCreditsDto dto)
        {
            // 🔹 Transaction start
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                foreach (var credit in dto.Credits)
                {

                    if (credit.CreditId.HasValue)
                    {
                        var find_credit_H1 = await _context.SubjectCredits
      .FirstOrDefaultAsync(a => a.CreditsId == credit.CreditId.Value && a.Head == "H1");
                        if (find_credit_H1 != null)
                        {
                            if (credit.ExamType.Any())
                            {
                                // Update fields
                                find_credit_H1.HeadType = string.Join(' ', credit.ExamType); // or InternalType based on logic
                                find_credit_H1.HeadOutOf = credit.ExamOutOf;
                                find_credit_H1.HeadPass = credit.ExamPassing;
                                find_credit_H1.HeadFormula = credit.PassingPercentage;
                                // ...set other fields as needed

                                //Update The Changes
                                _context.SubjectCredits.Update(find_credit_H1);
                            }
                            else
                            {
                                // 🔹 4. Delete SubjectCredit
                                await _genericRepository.DeleteAsync<SubjectCredits>(find_credit_H1.Id);
                            }

                         
                        }
                        else
                        {
                            if (credit.ExamType.Any())
                            {
                                var CreditsH1 = new SubjectCredits
                                {
                                    Id = Guid.NewGuid(),
                                    CreditsId = credit.CreditId,
                                    Head = "H1",
                                    HeadType = string.Join(" ", credit.ExamType),
                                    HeadOutOf = credit.ExamOutOf,
                                    HeadPass = credit.ExamPassing,
                                    HeadFormula = credit.PassingPercentage,
                                };
                                _context.SubjectCredits.Add(CreditsH1);
                            }
                        }
                        var find_credit_H2 = await _context.SubjectCredits
      .FirstOrDefaultAsync(a => a.CreditsId == credit.CreditId.Value && a.Head == "H2");
                        if (find_credit_H2 != null)
                        {
                            if (credit.InternalType.Any())
                            {
                                // Update fields
                                find_credit_H2.HeadType = string.Join(' ', credit.InternalType); // or InternalType based on logic
                                find_credit_H2.HeadOutOf = credit.InternalOutOf;
                                find_credit_H2.HeadPass = credit.InternalPassing;
                                find_credit_H2.HeadFormula = credit.PassingPercentage;
                                // ...set other fields as needed

                                //Update The Changes
                                _context.SubjectCredits.Update(find_credit_H2);
                            }
                            else
                            {
                                // 🔹 4. Delete SubjectCredit
                                await _genericRepository.DeleteAsync<SubjectCredits>(find_credit_H2.Id);
                            }
                             
                        }
                        else
                        {
                            if (credit.InternalType.Any())
                            {
                                var CreditsH2 = new SubjectCredits
                                {
                                    Id = Guid.NewGuid(),
                                    CreditsId = credit.CreditId,
                                    Head = "H2",
                                    HeadType = string.Join(" ", credit.InternalType),
                                    HeadOutOf = credit.InternalOutOf,
                                    HeadPass = credit.InternalPassing,
                                    HeadFormula = credit.PassingPercentage,
                                };
                                _context.SubjectCredits.Add(CreditsH2);
                            }
                        }


                        //Update Credits in SubjectCreditMasters
                        var subjectcredits = await _context.SubjectCreditMasters.FirstOrDefaultAsync(a => a.CreditsId == credit.CreditId);
                        if (subjectcredits != null)
                        {
                            subjectcredits.TotalCredits = credit.CreditNo;
                            _context.SubjectCreditMasters.Update(subjectcredits);
                        }
                    }
                }
                _context.SaveChanges();
                // 🔹 Commit transaction
                await transaction.CommitAsync();

                return new ApiResponseDto<object>
                {
                    Success = true,
                    Message = "Credit Updated successfully",
                    Data = null
                };
            }
            catch (Exception ex)
            {
                // 🔴 Rollback if anything fails
                await transaction.RollbackAsync();

                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = "Error while saving subject",
                    Data = ex.Message
                };
            }
        }
        public async Task<ApiResponseDto<object>> DeleteCreditAsync(DeleteCreditDto dto)
        {
            // 🔹 Transaction start
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // 🔹 1. Get all CreditMaster IDs for subject
                var creditMasterIds = await _context.SubjectCreditMasters
                    .Where(x => x.SubjectId == dto.SubjectId && x.AYID==dto.Ayid)
                    .Select(x => x.CreditsId)
                    .ToListAsync();

                if (creditMasterIds.Any())
                {
                    // 🔹 2. Delete SubjectCredits (child)
                    await _genericRepository.DeleteRangeAsync<SubjectCredits>(
    x => x.CreditsId.HasValue &&
         creditMasterIds.Contains(x.CreditsId.Value)
);

                    // 🔹 3. Delete SubjectCreditMasters (parent)
                    await _genericRepository.DeleteRangeAsync<SubjectCreditMaster>(
                        x => creditMasterIds.Contains(x.CreditsId)
                    );
                }
                _context.SaveChanges();
                await transaction.CommitAsync();

                return new ApiResponseDto<object>
                {
                    Success = true,
                    Message = "Credit Deleted successfully",
                    Data = null
                };
            }
            catch (Exception ex)
            {
                // 🔴 Rollback if anything fails
                await transaction.RollbackAsync();

                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = "Error while saving subject",
                    Data = ex.Message
                };
            }
        }
        public async Task<ApiResponseDto<object>> DeleteSubjectAsync(DeleteSubjectDto dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // 🔹 1. Get all CreditMaster IDs for subject
                var creditMasterIds = await _context.SubjectCreditMasters
                    .Where(x => x.SubjectId == dto.SubjectId)
                    .Select(x => x.CreditsId)
                    .ToListAsync();

                if (creditMasterIds.Any())
                {
                    // 🔹 2. Delete SubjectCredits (child)
                    await _genericRepository.DeleteRangeAsync<SubjectCredits>(
    x => x.CreditsId.HasValue &&
         creditMasterIds.Contains(x.CreditsId.Value)
);

                    // 🔹 3. Delete SubjectCreditMasters (parent)
                    await _genericRepository.DeleteRangeAsync<SubjectCreditMaster>(
                        x => creditMasterIds.Contains(x.CreditsId)
                    );
                }

                // 🔹 4. Delete Subject
                await _genericRepository.DeleteAsync<SubjectMaster>(dto.SubjectId);

                // 🔹 5. Save ONCE
                await _context.SaveChangesAsync();

                // 🔹 Commit
                await transaction.CommitAsync();

                return new ApiResponseDto<object>
                {
                    Success = true,
                    Message = "Subject and credits deleted successfully",
                    Data = null
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = "Error while deleting subject",
                    Data = ex.Message
                };
            }
        }
        public async Task<ApiResponseDto<object>> SavePreviousCreditAsync(PreviousCredits dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // 🔹 Check if already copied
                bool alreadyExists = await _context.SubjectCreditMasters
                    .AnyAsync(x => x.SubjectId == dto.SubjectId && x.AYID == dto.Ayid);

                if (alreadyExists)
                {
                    return new ApiResponseDto<object>
                    {
                        Success = false,
                        Message = "Credits already exist for selected academic year",
                        Data = null
                    };
                }

                // 🔹 Load previous year credits
                var previousCredits =  _context.SubjectCreditMasters
                    .Where(x => x.SubjectId == dto.SubjectId && x.AYID == dto.PreAyid);

                foreach (var credit in previousCredits)
                {
                    var newCreditMasterId = Guid.NewGuid();

                    var currCreditMaster = new SubjectCreditMaster
                    {
                        CreditsId = newCreditMasterId,
                        TotalCredits = credit.TotalCredits,
                        AYID = dto.Ayid,
                        SubjectId = dto.SubjectId
                    };

                    _context.SubjectCreditMasters.Add(currCreditMaster);

                    // 🔹 Copy subject credits
                    var subjectCredits =  _context.SubjectCredits
                        .Where(x => x.CreditsId == credit.CreditsId);

                    foreach (var subcred in subjectCredits)
                    {
                        _context.SubjectCredits.Add(new SubjectCredits
                        {
                            Id = Guid.NewGuid(),
                            Head = subcred.Head,
                            HeadType = subcred.HeadType,
                            HeadOutOf = subcred.HeadOutOf,
                            HeadPass = subcred.HeadPass,
                            HeadFormula = subcred.HeadFormula,
                            CreditsId = newCreditMasterId
                        });
                    }
                }

                // 🔹 Save once
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return new ApiResponseDto<object>
                {
                    Success = true,
                    Message = "Previous year credits copied successfully",
                    Data = null
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = "Error while copying previous credits",
                    Data = ex.Message
                };
            }
        }
        public async Task<ApiResponseDto<object>> GetPreviousCreditAsync(PreviousCredits dto)
        {
            try
            {
                var CurrentCredits = _context.SubjectCreditMasters.Where(x => x.SubjectId == dto.SubjectId && x.AYID == dto.Ayid);
                if (CurrentCredits.Any())
                {
                    return new ApiResponseDto<object>
                    {
                        Success = false,
                        Message = "Current year credits exist",
                        Data = null
                    };
                }
                var PreviousCredits = _context.SubjectCreditMasters.Where(x => x.SubjectId == dto.SubjectId && x.AYID == dto.PreAyid);
                if (PreviousCredits.Any())
                {
                    return new ApiResponseDto<object>
                    {
                        Success = true,
                        Message = "Previous year credits exist",
                        Data = null
                    };
                }
                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = "Previous year credits not exist",
                    Data = null
                };

            }
            catch (Exception ex)
            {
                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = "Error while getting previous credits",
                    Data = ex.Message
                };
            }
        }
        public async Task<ApiResponseDto<object>> CheckCreditAsync(GetCredits dto)
        {
            try
            {
                var CurrentCredits = _context.SubjectCreditMasters.Where(x => x.SubjectId == dto.SubjectId && x.AYID == dto.Ayid);
                if (CurrentCredits.Any())
                {
                    return new ApiResponseDto<object>
                    {
                        Success = true,
                        Message = "Current year credits exist",
                        Data = null
                    };
                }
                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = "Current year credits not exist",
                    Data = null
                };

            }
            catch (Exception ex)
            {
                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = "Error while getting credits",
                    Data = ex.Message
                };
            }
        }
        public async Task<ApiResponseDto<object>> VerifyCreditAccess(VerifyCreditAccessDto dto)
        {
           
            if (dto.Password=="VSS")
            {
                return new ApiResponseDto<object>
                {
                    Success = true,
                    Message = "Access granted"
                };
            }

            return new ApiResponseDto<object>
            {

                Success = false,
                Message = "Invalid password"
            };
        }
    }
    
}
