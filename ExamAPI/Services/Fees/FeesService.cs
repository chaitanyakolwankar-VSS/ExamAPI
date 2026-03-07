using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Fees
{
    public class FeesService : IFeesService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<FeesService> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public FeesService(
            ApplicationDbContext context, IHttpContextAccessor httpContextAccessor,
            ILogger<FeesService> logger)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }


        // ================= GET BRANCHES =================

        public async Task<List<BranchOptionDto>> GetCoursesAsync()
        {
            var branchList = await _context.CourseMasters
                .Where(c => !string.IsNullOrEmpty(c.Name))
                .Select(c => new BranchOptionDto
                {
                    Value = c.CourseId.ToString(),
                    Label = c.Name
                })
                .OrderBy(b => b.Label)
                .ToListAsync();

            return branchList;
        }

        // ================= GET EXAMS =================

        public async Task<List<ExamOptionDto>> GetExamsAsync(Guid ayid, Guid? courseId)
        {
            // If either ayid or courseId is invalid, return empty list immediately
            if (ayid == Guid.Empty || !courseId.HasValue || courseId == Guid.Empty)
            {
                return new List<ExamOptionDto>();
            }

            var query = _context.Exams
                .Where(e => !e.IsDeleted
                            && e.AcademicYearAYID == ayid
                            && e.CourseId.HasValue
                            && e.CourseId.Value == courseId.Value);

            var exams = await query
                .OrderBy(e => e.Name)
                .Select(e => new
                {
                    e.ExamId,
                    e.Name,
                    e.ExamType
                })
                .ToListAsync();

            return exams.Select(e => new ExamOptionDto
            {
               Ayid= ayid,
               CourseId= courseId,
                Value = e.ExamId.ToString(),
                Label = string.IsNullOrEmpty(e.ExamType)
                    ? e.Name
                    : $"{e.Name} ({e.ExamType})"
            }).ToList();
        }

        // ================= GET CATEGORIES =================

        public async Task<List<CategoryOptionDto>> GetCategoriesAsync()
        {
            return await _context.StudentMasters
                .Where(s => !string.IsNullOrEmpty(s.Category))
                .Select(s => s.Category!)
                .Distinct()
                .OrderBy(c => c)
                .Select(c => new CategoryOptionDto
                {
                    Value = c,
                    Label = c
                })
                .ToListAsync();
        }

        // ================= GET FEES =================

        public async Task<List<FeesRecordDto>> GetFeesByExamAsync(GetFeesDto dto)
        {
            Guid? eId = string.IsNullOrEmpty(dto.ExamId) ? null : Guid.Parse(dto.ExamId);
            Guid? cId = string.IsNullOrEmpty(dto.CourseId) ? null : Guid.Parse(dto.CourseId);

            var data = await _context.FeesDefines
                .Where(f => f.IsDeleted == false 
                         && f.ExamId == eId                    
                         && f.Category == dto.Category
                         && f.CourseId == cId
                         && f.SemId == dto.SemId
                         && f.ExamType == dto.ExamType)
                .ToListAsync();
            return data
                .OrderBy(f =>
                {
                    var numericPart = new string(f.SubCount.Where(char.IsDigit).ToArray());
                    return int.TryParse(numericPart, out int val) ? val : 0;
                })
                .Select(f => new FeesRecordDto
                {
                    
                    Amount = f.Amount,
                    SubCount = f.SubCount
                })
                .ToList();
        }

        // ================= SAVE FEES =================
        public async Task<ApiResponseDto<object>> SaveFeesAsync(SaveFees dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // GUID Conversions
                Guid examId = Guid.Parse(dto.ExamId);
                Guid ayid = Guid.Parse(dto.Ayid);
                Guid? courseId = string.IsNullOrEmpty(dto.CourseId) ? null : Guid.Parse(dto.CourseId);

                // Pehle check karein ke kya is category/exam ka data pehle se hai (Duplicate Prevention)
                var existing = await _context.FeesDefines
                    .Where(f => f.ExamId == examId && f.Category == dto.Category && f.CourseId == courseId && f.SemId == dto.SemId && f.ExamType == dto.ExamType)
                    .ToListAsync();

                if (existing.Any())
                {
                    foreach (var e in existing)
                    {
                        e.IsDeleted = true;
                        e.UpdatedAt = DateTime.Now;
                    }
                }
                var userIdString = _httpContextAccessor.HttpContext?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                Guid? currentUserId = !string.IsNullOrEmpty(userIdString) ? Guid.Parse(userIdString) : null;
                var sortedAmounts = dto.Amount
     .OrderBy(a =>
     {
         var numeric = new string(a.RowSubCount.Where(char.IsDigit).ToArray());
         return int.TryParse(numeric, out int v) ? v : 0;
     }).ToList();



                for (int i = 0; i < sortedAmounts.Count; i++)
                {
                    var fee = sortedAmounts[i];
                    string semCountStr = (i + 1).ToString();


                    if (i == dto.Amount.Count - 1)
                    {
                        semCountStr += "+";
                    }

                    var entity = new FeesDefines
                    {
                        FeesDefineId = Guid.NewGuid(),
                        Ayid = ayid,
                        ExamId = examId,
                        CourseId = courseId,
                        ExamType = dto.ExamType,
                        SemId = dto.SemId,
                        Category = dto.Category,
                        SubCount = semCountStr,
                        Amount = fee.Amount,
                        CreatedAt = DateTime.Now,
                        CreatedBy = currentUserId
                    };

                    await _context.FeesDefines.AddAsync(entity);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return new ApiResponseDto<object> { Success = true, Message = "Fees Saved Successfully" };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new ApiResponseDto<object> { Success = false, Message = "Error: " + ex.Message };
            }
        }

        public async Task<ApiResponseDto<object>> DeleteFeesAsync(DeleteFeesDto dto)
        {
            Guid? eId = string.IsNullOrEmpty(dto.ExamId) ? null : Guid.Parse(dto.ExamId);
            Guid? cId = string.IsNullOrEmpty(dto.CourseId) ? null : Guid.Parse(dto.CourseId);

            var feesToHide = await _context.FeesDefines
                    .Where(f => f.ExamId == eId
                             && f.Category ==dto.Category
                             && f.CourseId == cId
                             && f.IsDeleted == false) 
                    .ToListAsync();

                if (!feesToHide.Any())
                    return new ApiResponseDto<object> { Success = false, Message = "No active records found" };

                foreach (var fee in feesToHide)
                {
                    fee.IsDeleted = true; // Hard delete nahi, sirf flag change
                    fee.UpdatedAt = DateTime.Now; // Optional tracking
                }

                await _context.SaveChangesAsync();
                return new ApiResponseDto<object> { Success = true, Message = "Fees removed successfully" };
            
           
        }
    }
}