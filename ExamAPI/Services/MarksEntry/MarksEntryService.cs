using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Result.Engine;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ExamAPI.Services.MarksEntry
{
    public class MarksEntryService : IMarksEntryService
    {
        private readonly ApplicationDbContext _context;

        public MarksEntryService(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <summary>What staff type into a marks box to record an absence.</summary>
        private const string AbsentInput = "Ab";
        private const string ResolutionSymbol = "^";

        public async Task<ApiResponseDto<IEnumerable<MarksEntryDataDto>>> GetMarksEntryDataAsync(MarksEntryFilterRequest request, Guid collegeId)
        {
            try
            {
                var query = _context.MarksMasters
                    .Include(mm => mm.Student)
                    .Include(mm => mm.StudentMarks)
                        .ThenInclude(sm => sm.CreditMaster)
                            .ThenInclude(cm => cm.Credits)
                    .Where(mm => mm.ExamId == request.ExamId
                        && mm.SemesterId == request.SemId
                        && mm.Pattern == request.Pattern
                        && mm.Exam != null
                        && mm.Exam.CourseId == request.BranchId
                        && mm.Student != null
                        && mm.Student.CollegeId == collegeId
                        && !mm.IsDeleted);

                if (!string.IsNullOrEmpty(request.StudentId))
                {
                    query = query.Where(mm => mm.StudentID == request.StudentId);
                }

                // If groupId is provided, filter StudentMarks by subject within that group
                // The current schema has SubjectId in StudentMarks.
                
                var marksRecords = await query.ToListAsync();

                var resolutionLimits = await GetResolutionLimitsAsync(request.ExamId, marksRecords);

                var result = marksRecords.Select(mm =>
                {
                    var subjectHeads = mm.StudentMarks?.Where(sm => sm.SubjectId == request.SubjectId).ToList()
                        ?? new List<StudentMarks>();
                    var creditMaster = subjectHeads.Select(sm => sm.CreditMaster).FirstOrDefault(cm => cm != null);

                    return new MarksEntryDataDto
                    {
                        MarksId = mm.MarksId,
                        StudentId = mm.StudentID ?? "N/A",
                        StudentName = mm.Student != null ? $"{mm.Student.FirstName} {mm.Student.LastName}" : "N/A",
                        SeatNo = mm.SeatNo ?? "N/A",
                        Rank = mm.Rank ?? 0,
                        PassingStrategy = creditMaster?.PassingStrategy ?? PassingStrategies.HeadWise,
                        PassPercentage = creditMaster?.PassPercentage,
                        Heads = subjectHeads.Select(sm =>
                        {
                            var credit = SubjectPassEvaluator.FindCredit(sm);
                            return new StudentHeadMarksDto
                            {
                                StudentMarksId = sm.Id,
                                CreditId = sm.CreditsId ?? Guid.Empty,
                                SubjectCreditId = credit?.Id ?? Guid.Empty,
                                HeadName = SubjectPassEvaluator.GetHeadLabel(sm),
                                Marks = sm.IsAbsent ? AbsentInput : sm.Marks?.ToString() ?? "",
                                OutOf = SubjectPassEvaluator.GetHeadOutOf(sm),
                                Passing = SubjectPassEvaluator.GetHeadPass(sm),
                                Grace = sm.Grace,
                                IsAbsent = sm.IsAbsent,
                                IsPassed = !sm.IsAbsent && SubjectPassEvaluator.IsHeadPassed(sm),
                                Resolution = credit != null && resolutionLimits.TryGetValue(credit.Id, out var limit) ? limit : null,
                                // A carried-forward head (ATKT/Revaluation) is not being re-sat, so
                                // its mark is fixed -- lock it for entry. Fresh heads stay editable.
                                IsCarryForward = sm.IsCarryForward,
                                IsEnabled = !sm.IsCarryForward
                            };
                        }).ToList()
                    };
                }).OrderBy(r => r.SeatNo).ToList();

                return new ApiResponseDto<IEnumerable<MarksEntryDataDto>> { Success = true, Data = result };
            }
            catch (Exception ex)
            {
                return new ApiResponseDto<IEnumerable<MarksEntryDataDto>> { Success = false, Message = $"Error: {ex.Message}" };
            }
        }

        public async Task<ApiResponseDto<object>> SaveMarksAsync(SaveMarksRequest request, Guid collegeId)
        {
            try
            {
                var updates = request.Updates?
                    .GroupBy(update => update.StudentMarksId)
                    .Select(group => group.Last())
                    .ToList() ?? new List<StudentMarksUpdateDto>();

                if (!updates.Any() && request.Resolutions.Count == 0)
                {
                    return new ApiResponseDto<object> { Success = false, Message = "No marks updates were supplied." };
                }

                var requestedIds = updates.Select(update => update.StudentMarksId).ToList();
                var studentMarks = await _context.StudentMarks
                    .Include(sm => sm.MarksMaster)
                        .ThenInclude(mm => mm!.Student)
                    .Include(sm => sm.CreditMaster)
                        .ThenInclude(cm => cm!.Credits)
                    .Where(sm => requestedIds.Contains(sm.Id)
                        && sm.MarksMaster != null
                        && sm.MarksMaster.Student != null
                        && sm.MarksMaster.Student.CollegeId == collegeId)
                    .ToDictionaryAsync(sm => sm.Id);

                if (studentMarks.Count != requestedIds.Count)
                {
                    return new ApiResponseDto<object> { Success = false, Message = "One or more marks entries are unavailable for the current college." };
                }

                var examId = request.ExamId != Guid.Empty
                    ? request.ExamId
                    : studentMarks.Values.FirstOrDefault()?.MarksMaster?.ExamId;
                if (examId.HasValue)
                {
                    var exam = await _context.Exams.FirstOrDefaultAsync(e => e.ExamId == examId);
                    if (exam != null && exam.IsLocked)
                    {
                        return new ApiResponseDto<object> { Success = false, Message = "This exam is locked. Further marks entry edits are not allowed." };
                    }
                }

                var marksMasters = new HashSet<MarksMaster>();
                foreach (var update in updates)
                {
                    var studentMark = studentMarks[update.StudentMarksId];
                    var error = ApplyMarkAsync(studentMark, update.Marks);
                    if (error != null)
                    {
                        return new ApiResponseDto<object> { Success = false, Message = error };
                    }

                    if (studentMark.MarksMaster != null)
                    {
                        marksMasters.Add(studentMark.MarksMaster);
                    }
                }

                foreach (var marksMaster in marksMasters)
                {
                    marksMaster.Rank = request.Rank;
                }

                // Resolution is a marks-entry decision: its config is set on this screen and
                // applied here, so the '^' is already on the marks before results are processed.
                await UpsertResolutionsAsync(request, collegeId);
                await ApplySubjectResolutionsAsync(request.ExamId, request.SubjectId, collegeId);

                await _context.SaveChangesAsync();
                return new ApiResponseDto<object> { Success = true, Message = "Marks saved successfully." };
            }
            catch (Exception ex)
            {
                return new ApiResponseDto<object> { Success = false, Message = $"Error: {ex.Message}" };
            }
        }

        /// <summary>The condonation limit configured per SubjectCredits row for this exam.</summary>
        private async Task<Dictionary<Guid, int>> GetResolutionLimitsAsync(Guid examId, IEnumerable<MarksMaster> marksRecords)
        {
            var creditIds = marksRecords
                .SelectMany(mm => mm.StudentMarks ?? Enumerable.Empty<StudentMarks>())
                .Where(sm => sm.CreditsId.HasValue)
                .Select(sm => sm.CreditsId!.Value)
                .Distinct()
                .ToList();

            var resolutions = await _context.Resolution
                .Where(r => r.ExamID == examId && !r.IsDeleted && r.CreditID.HasValue && creditIds.Contains(r.CreditID.Value))
                .ToListAsync();

            return resolutions
                .Where(r => int.TryParse(r.Resolution, out var limit) && limit > 0)
                .GroupBy(r => r.SubjectCreditID)
                .ToDictionary(g => g.Key, g => int.Parse(g.First().Resolution!));
        }

        /// <summary>
        /// Writes the resolution limits entered on the marks-entry screen into ResolutionMaster.
        /// This config used to be set in Exam Master; it now lives with the marks it condones.
        /// </summary>
        private async Task UpsertResolutionsAsync(SaveMarksRequest request, Guid collegeId)
        {
            if (request.Resolutions.Count == 0 || request.ExamId == Guid.Empty) return;

            var subjectCreditIds = request.Resolutions.Select(r => r.SubjectCreditId).ToList();
            var existing = await _context.Resolution
                .Where(r => r.ExamID == request.ExamId && subjectCreditIds.Contains(r.SubjectCreditID))
                .ToListAsync();

            var configuredHeads = await _context.SubjectCredits
                .Where(c => subjectCreditIds.Contains(c.Id))
                .ToListAsync();

            var academicYearId = await _context.MarksMasters
                .Where(mm => mm.ExamId == request.ExamId && mm.Student!.CollegeId == collegeId)
                .Select(mm => mm.AcademicYearAYID)
                .FirstOrDefaultAsync();

            foreach (var update in request.Resolutions)
            {
                var row = existing.FirstOrDefault(r => r.SubjectCreditID == update.SubjectCreditId && !r.IsDeleted);
                if (row == null)
                {
                    var head = configuredHeads.FirstOrDefault(c => c.Id == update.SubjectCreditId);
                    if (head == null) continue;

                    row = new ResolutionMaster
                    {
                        ID = Guid.NewGuid(),
                        ExamID = request.ExamId,
                        CreditID = head.CreditsId,
                        SubjectCreditID = update.SubjectCreditId,
                        Head = head.Head,
                        AYID = academicYearId,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.Resolution.Add(row);
                }

                row.Resolution = (update.Resolution ?? 0).ToString();
                row.UpdatedAt = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Applies resolution across every subject the save touched. The client only sends the
        /// heads it changed, so the sibling heads are reloaded to judge the subject as a whole.
        /// </summary>
        private async Task ApplySubjectResolutionsAsync(Guid examId, Guid subjectId, Guid collegeId)
        {
            if (examId == Guid.Empty || subjectId == Guid.Empty) return;

            var allHeads = await _context.StudentMarks
                .Include(sm => sm.MarksMaster)
                .Include(sm => sm.CreditMaster)
                    .ThenInclude(cm => cm!.Credits)
                .Where(sm => sm.SubjectId == subjectId
                    && sm.MarksMaster!.ExamId == examId
                    && sm.MarksMaster.Student!.CollegeId == collegeId)
                .ToListAsync();

            if (allHeads.Count == 0) return;

            var creditIds = allHeads.Where(sm => sm.CreditsId.HasValue).Select(sm => sm.CreditsId!.Value).Distinct().ToList();
            var resolutionRows = await _context.Resolution
                .Where(r => r.ExamID == examId && !r.IsDeleted && r.CreditID.HasValue && creditIds.Contains(r.CreditID.Value))
                .ToListAsync();

            var resolutionLimits = resolutionRows
                .Where(r => int.TryParse(r.Resolution, out var limit) && limit > 0)
                .GroupBy(r => r.SubjectCreditID)
                .ToDictionary(g => g.Key, g => int.Parse(g.First().Resolution!));

            foreach (var group in allHeads.GroupBy(sm => sm.MarksId))
            {
                ApplyResolutionAsync(group.OrderBy(sm => sm.Head).ToList(), resolutionLimits);
            }
        }

        /// <summary>
        /// Records what the staff typed for one head. Raw input only -- resolution is applied
        /// afterwards by <see cref="ApplyResolutionAsync"/>, which needs the whole subject.
        /// </summary>
        private static string? ApplyMarkAsync(StudentMarks studentMark, string? input)
        {
            var value = input?.Trim();
            studentMark.Resolution = null;
            studentMark.Grace = null;
            studentMark.IsAbsent = false;
            studentMark.UpdatedAt = DateTime.UtcNow;

            if (string.IsNullOrEmpty(value))
            {
                studentMark.RawMarks = null;
                studentMark.Marks = null;
                return null;
            }

            if (string.Equals(value, AbsentInput, StringComparison.OrdinalIgnoreCase))
            {
                studentMark.RawMarks = null;
                studentMark.Marks = null;
                studentMark.IsAbsent = true;
                return null;
            }

            if (!int.TryParse(value, out var marks))
            {
                return $"Validation Error: Marks for {SubjectPassEvaluator.GetHeadLabel(studentMark)} must be a whole number or Ab.";
            }

            var credit = SubjectPassEvaluator.FindCredit(studentMark);
            if (credit == null
                || !int.TryParse(credit.HeadOutOf, out var outOf)
                || !int.TryParse(credit.HeadPass, out _))
            {
                return $"Configuration Error: Passing and maximum marks are not configured for {SubjectPassEvaluator.GetHeadLabel(studentMark)}.";
            }

            if (marks < 0 || marks > outOf)
            {
                return $"Validation Error: Marks ({marks}) for {SubjectPassEvaluator.GetHeadLabel(studentMark)} must be between 0 and {outOf}.";
            }

            studentMark.RawMarks = marks;
            studentMark.Marks = marks;
            return null;
        }

        /// <summary>
        /// Applies the staff's condonation to one subject, bumping the head(s) they configured
        /// resolution on and marking them with '^'.
        /// <para>
        /// Head-wise closes each failing head's own shortfall; combined closes the subject
        /// deficit across the configured heads, each capped by its limit and its headroom.
        /// </para>
        /// </summary>
        private static void ApplyResolutionAsync(IList<StudentMarks> subjectHeads, IReadOnlyDictionary<Guid, int> resolutionLimits)
        {
            var verdict = SubjectPassEvaluator.Evaluate(subjectHeads);
            if (verdict.IsPassed || verdict.IsAllAbsent) return;

            var remainingDeficit = verdict.Deficit;

            foreach (var sm in subjectHeads)
            {
                if (remainingDeficit <= 0) break;
                if (sm.IsAbsent || !sm.Marks.HasValue) continue;

                var credit = SubjectPassEvaluator.FindCredit(sm);
                if (credit == null || !resolutionLimits.TryGetValue(credit.Id, out var limit) || limit <= 0) continue;

                var headPass = SubjectPassEvaluator.GetHeadPass(sm);
                var headRoom = verdict.IsCombined
                    ? SubjectPassEvaluator.GetHeadOutOf(sm) - sm.Marks.Value
                    : headPass - sm.Marks.Value;

                var bump = Math.Min(Math.Min(limit, headRoom), remainingDeficit);
                if (bump <= 0) continue;

                sm.Resolution = bump;
                sm.Marks = (sm.RawMarks ?? 0) + bump;
                sm.Grace = ResolutionSymbol;
                sm.UpdatedAt = DateTime.UtcNow;
                remainingDeficit -= bump;
            }

            // All or nothing: a partial bump would leave the subject failing while showing '^'.
            if (remainingDeficit > 0)
            {
                foreach (var sm in subjectHeads.Where(sm => sm.Resolution.HasValue))
                {
                    sm.Marks = sm.RawMarks;
                    sm.Resolution = null;
                    sm.Grace = null;
                }
            }
        }

        public async Task<byte[]> ExportTemplateExcelAsync(MarksEntryFilterRequest request, Guid collegeId)
        {
            ExcelPackage.License.SetNonCommercialPersonal("ReactApi Project");

            var dataResult = await GetMarksEntryDataAsync(request, collegeId);
            if (!dataResult.Success || dataResult.Data == null || !dataResult.Data.Any())
            {
                return Array.Empty<byte>();
            }

            var marksData = dataResult.Data.ToList();
            var subject = await _context.SubjectMasters.FindAsync(request.SubjectId);
            var exam = await _context.Exams.FindAsync(request.ExamId);

            using (var package = new ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add("Marks Entry");

                // Title
                worksheet.Cells["A1:F1"].Merge = true;
                worksheet.Cells["A1"].Value = "MARKS ENTRY TEMPLATE";
                worksheet.Cells["A1"].Style.Font.Size = 16;
                worksheet.Cells["A1"].Style.Font.Bold = true;
                worksheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                worksheet.Cells["A1"].Style.Font.Color.SetColor(Color.DarkBlue);

                // Header Information
                worksheet.Cells["A3"].Value = "Exam:";
                worksheet.Cells["B3"].Value = exam?.Name ?? "N/A";
                worksheet.Cells["A4"].Value = "Subject:";
                worksheet.Cells["B4"].Value = subject?.Name ?? "N/A";
                worksheet.Cells["A3:A4"].Style.Font.Bold = true;

                // Column Headers
                worksheet.Cells[6, 1].Value = "Seat No";
                worksheet.Cells[6, 2].Value = "Student ID";
                worksheet.Cells[6, 3].Value = "Student Name";

                var heads = marksData.First().Heads.OrderBy(h => h.HeadName).ToList();
                int col = 4;
                foreach (var head in heads)
                {
                    worksheet.Cells[6, col].Value = $"{head.HeadName} (Out Of: {head.OutOf})";
                    worksheet.Cells[5, col].Value = head.HeadName; // Raw head name for matching
                    worksheet.Cells[5, col].Style.Font.Color.SetColor(Color.White); // Hide it by making it white or keep it visible
                    
                    // Hidden column for StudentMarksId
                    worksheet.Cells[6, col + 1].Value = $"{head.HeadName}_ID";
                    worksheet.Column(col + 1).Hidden = true;
                    col += 2;
                }

                // Style the Header Row
                using (var range = worksheet.Cells[6, 1, 6, col - 1])
                {
                    range.Style.Font.Bold = true;
                    range.Style.Font.Color.SetColor(Color.White);
                    range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(41, 128, 185)); // Professional Blue
                    range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    range.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                }

                // Data
                int row = 7;
                foreach (var student in marksData)
                {
                    worksheet.Cells[row, 1].Value = student.SeatNo;
                    worksheet.Cells[row, 2].Value = student.StudentId;
                    worksheet.Cells[row, 3].Value = student.StudentName;

                    int sCol = 4;
                    foreach (var head in student.Heads.OrderBy(h => h.HeadName))
                    {
                        worksheet.Cells[row, sCol].Value = head.Marks;
                        worksheet.Cells[row, sCol].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        
                        // Highlight missing/empty marks with light yellow for data entry focus
                        if (string.IsNullOrEmpty(head.Marks))
                        {
                            worksheet.Cells[row, sCol].Style.Fill.PatternType = ExcelFillStyle.Solid;
                            worksheet.Cells[row, sCol].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 253, 208)); // Cream/Light Yellow
                        }

                        worksheet.Cells[row, sCol + 1].Value = head.StudentMarksId.ToString();
                        sCol += 2;
                    }
                    row++;
                }

                // Apply borders to the entire data table
                using (var range = worksheet.Cells[6, 1, row - 1, col - 1])
                {
                    range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                    range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                    range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                    range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                    range.Style.Border.Top.Color.SetColor(Color.Gray);
                    range.Style.Border.Bottom.Color.SetColor(Color.Gray);
                    range.Style.Border.Left.Color.SetColor(Color.Gray);
                    range.Style.Border.Right.Color.SetColor(Color.Gray);
                }

                // Add Data Validations to columns
                int vCol = 4;
                foreach (var head in heads)
                {
                    var valAddress = ExcelCellBase.GetAddress(7, vCol, Math.Max(7, row - 1), vCol);
                    var validation = worksheet.DataValidations.AddCustomValidation(valAddress);
                    var cellAddress = ExcelCellBase.GetAddress(7, vCol);
                    
                    validation.Formula.ExcelFormula = $"OR(EXACT({cellAddress}, \"Ab\"), EXACT({cellAddress}, \"ab\"), AND(ISNUMBER({cellAddress}), {cellAddress}>=0, {cellAddress}<={head.OutOf}))";
                    validation.ShowErrorMessage = true;
                    validation.ErrorStyle = OfficeOpenXml.DataValidation.ExcelDataValidationWarningStyle.stop;
                    validation.ErrorTitle = "Invalid Marks Entry";
                    validation.Error = $"Marks must be between 0 and {head.OutOf}, or 'Ab' for absent.";
                    
                    vCol += 2;
                }

                worksheet.Cells.AutoFitColumns();
                return await package.GetAsByteArrayAsync();
            }
        }

        public async Task<ApiResponseDto<object>> ImportMarksExcelAsync(Guid examId, Guid subjectId, byte[] fileBytes, Guid collegeId)
        {
            try
            {
                var exam = await _context.Exams.FirstOrDefaultAsync(e => e.ExamId == examId);
                if (exam != null && exam.IsLocked)
                {
                    return new ApiResponseDto<object> { Success = false, Message = "This exam is locked. Further marks imports are not allowed." };
                }
                
                ExcelPackage.License.SetNonCommercialPersonal("ReactApi Project");
                using (var stream = new MemoryStream(fileBytes))
                using (var package = new ExcelPackage(stream))
                {
                    var worksheet = package.Workbook.Worksheets[0];
                    int rowCount = worksheet.Dimension.Rows;
                    int colCount = worksheet.Dimension.Columns;

                    // Identify Head columns and their ID columns
                    var headColumns = new List<(int MarkCol, int IdCol)>();
                    for (int col = 4; col <= colCount; col++)
                    {
                        var headerValue = worksheet.Cells[6, col].Value?.ToString();
                        if (headerValue != null && headerValue.EndsWith("_ID"))
                        {
                            headColumns.Add((col - 1, col));
                        }
                    }

                    int updatedCount = 0;
                    for (int row = 7; row <= rowCount; row++)
                    {
                        foreach (var (markCol, idCol) in headColumns)
                        {
                            var idValue = worksheet.Cells[row, idCol].Value?.ToString();
                            if (Guid.TryParse(idValue, out Guid studentMarksId))
                            {
                                var markValue = worksheet.Cells[row, markCol].Value?.ToString()?.Trim();
                                var sm = await _context.StudentMarks
                                    .Include(x => x.MarksMaster)
                                        .ThenInclude(marksMaster => marksMaster!.Student)
                                    .Include(x => x.CreditMaster)
                                        .ThenInclude(creditMaster => creditMaster!.Credits)
                                    .FirstOrDefaultAsync(x => x.Id == studentMarksId
                                        && x.SubjectId == subjectId
                                        && x.MarksMaster != null
                                        && x.MarksMaster.ExamId == examId
                                        && x.MarksMaster.Student != null
                                        && x.MarksMaster.Student.CollegeId == collegeId);

                                if (sm == null)
                                {
                                    return new ApiResponseDto<object> { Success = false, Message = $"Import Error: Row {row} does not match the selected exam, subject, or college." };
                                }

                                var error = ApplyMarkAsync(sm, markValue);
                                if (error != null)
                                {
                                    return new ApiResponseDto<object> { Success = false, Message = $"Import Error: Row {row}. {error}" };
                                }

                                updatedCount++;
                            }
                        }
                    }

                    // An import is marks entry by another route, so resolution applies here too.
                    await ApplySubjectResolutionsAsync(examId, subjectId, collegeId);

                    await _context.SaveChangesAsync();
                    return new ApiResponseDto<object> { Success = true, Message = $"Successfully imported marks for {updatedCount} records." };
                }
            }
            catch (Exception ex)
            {
                return new ApiResponseDto<object> { Success = false, Message = $"Import failed: {ex.Message}" };
            }
        }
    }
}
