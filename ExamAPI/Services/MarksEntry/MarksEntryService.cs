using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
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

        public async Task<ApiResponseDto<IEnumerable<MarksEntryDataDto>>> GetMarksEntryDataAsync(MarksEntryFilterRequest request, Guid collegeId)
        {
            try
            {
                var query = _context.MarksMasters
                    .Include(mm => mm.Student)
                    .Include(mm => mm.StudentMarks)
                        .ThenInclude(sm => sm.CreditMaster)
                            .ThenInclude(cm => cm.Credits)
                    .Where(mm => mm.ExamId == request.ExamId && mm.SemesterId == request.SemId && mm.Pattern == request.Pattern && !mm.IsDeleted);

                if (!string.IsNullOrEmpty(request.StudentId))
                {
                    query = query.Where(mm => mm.StudentID == request.StudentId);
                }

                // If groupId is provided, filter StudentMarks by subject within that group
                // The current schema has SubjectId in StudentMarks.
                
                var marksRecords = await query.ToListAsync();

                var result = marksRecords.Select(mm => new MarksEntryDataDto
                {
                    MarksId = mm.MarksId,
                    StudentId = mm.StudentID ?? "N/A",
                    StudentName = mm.Student != null ? $"{mm.Student.FirstName} {mm.Student.LastName}" : "N/A",
                    SeatNo = mm.SeatNo ?? "N/A",
                    Rank = mm.Rank ?? 0,
                    Heads = mm.StudentMarks?
                        .Where(sm => sm.SubjectId == request.SubjectId)
                        .Select(sm => new StudentHeadMarksDto
                        {
                            StudentMarksId = sm.Id,
                            CreditId = sm.CreditsId ?? Guid.Empty,
                            HeadName = sm.Head ?? "N/A",
                            Marks = sm.Marks?.ToString() ?? (sm.Marks == null && sm.Remark == "Ab" ? "Ab" : ""),
                            OutOf = int.TryParse(sm.CreditMaster?.Credits?.FirstOrDefault(c => c.Head == sm.Head)?.HeadOutOf, out var o) ? o : 100,
                            Passing = int.TryParse(sm.CreditMaster?.Credits?.FirstOrDefault(c => c.Head == sm.Head)?.HeadPass, out var p) ? p : 40,
                            Grace = sm.Grace,
                            Remark = sm.Remark,
                            IsEnabled = true 
                        }).ToList() ?? new List<StudentHeadMarksDto>()
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
                HashSet<Guid> marksMasterIds = new HashSet<Guid>();

                foreach (var update in request.Updates)
                {
                    var sm = await _context.StudentMarks.FindAsync(update.StudentMarksId);
                    if (sm != null && sm.MarksId.HasValue)
                    {
                        marksMasterIds.Add(sm.MarksId.Value);
                        var mm = await _context.MarksMasters.FindAsync(sm.MarksId.Value);

                        if (update.Marks?.Trim().ToLower() == "ab")
                        {
                            sm.RawMarks = null;
                            sm.Marks = null;
                            sm.Remark = "Ab";
                        }
                        else if (int.TryParse(update.Marks, out var marks))
                        {
                            var outOf = 100;
                            var credit = sm.CreditMaster?.Credits?.FirstOrDefault(c => c.Head == sm.Head);
                            if (credit != null && int.TryParse(credit.HeadOutOf, out var o)) outOf = o;

                            if (marks < 0 || marks > outOf)
                            {
                                return new ApiResponseDto<object> { Success = false, Message = $"Validation Error: Marks ({marks}) for {sm.Head} cannot exceed Max Marks ({outOf})." };
                            }

                            sm.RawMarks = marks;
                            sm.Marks = marks;
                            
                            // Check for Passing and Resolution
                            var passing = 40;
                            var passCredit = sm.CreditMaster?.Credits?.FirstOrDefault(c => c.Head == sm.Head);
                            if (passCredit != null && int.TryParse(passCredit.HeadPass, out var p)) passing = p;

                            if (marks >= passing)
                            {
                                sm.Remark = "Successful";
                            }
                            else if (mm != null && mm.ExamId.HasValue)
                            {
                                // Check Resolution
                                var resolution = await _context.Resolution.FirstOrDefaultAsync(r => 
                                    r.ExamID == mm.ExamId && 
                                    r.CreditID == sm.CreditsId && 
                                    r.Head == sm.Head && 
                                    !r.IsDeleted);
                                
                                if (resolution != null && int.TryParse(resolution.Resolution, out var resLimit) && (passing - marks) <= resLimit)
                                {
                                    sm.Resolution = passing - marks;
                                    sm.Marks = passing;
                                    sm.Grace = (sm.Grace ?? "") + "^";
                                    sm.Remark = "Successful";
                                }
                                else
                                {
                                    sm.Remark = "Unsuccessful";
                                }
                            }
                            else
                            {
                                sm.Remark = "Unsuccessful";
                            }
                        }
                        else
                        {
                            sm.RawMarks = null;
                            sm.Marks = null;
                            sm.Remark = "Unsuccessful";
                        }
                        sm.UpdatedAt = DateTime.UtcNow;
                    }
                }

                // Update Rank in all affected MarksMasters
                foreach (var marksId in marksMasterIds)
                {
                    var mm = await _context.MarksMasters.FindAsync(marksId);
                    if (mm != null)
                    {
                        mm.Rank = request.Rank;
                    }
                }

                await _context.SaveChangesAsync();
                return new ApiResponseDto<object> { Success = true, Message = "Marks saved successfully." };
            }
            catch (Exception ex)
            {
                return new ApiResponseDto<object> { Success = false, Message = $"Error: {ex.Message}" };
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
                                    .Include(x => x.CreditMaster)
                                        .ThenInclude(c => c.Credits)
                                    .FirstOrDefaultAsync(x => x.Id == studentMarksId);

                                if (sm != null)
                                {
                                    if (string.IsNullOrEmpty(markValue))
                                    {
                                        sm.RawMarks = null;
                                        sm.Marks = null;
                                        sm.Remark = null;
                                    }
                                    else if (markValue.ToLower() == "ab")
                                    {
                                        sm.RawMarks = null;
                                        sm.Marks = null;
                                        sm.Remark = "Ab";
                                    }
                                    else if (int.TryParse(markValue, out int marks))
                                    {
                                        var outOf = 100;
                                        var credit = sm.CreditMaster?.Credits?.FirstOrDefault(c => c.Head == sm.Head);
                                        if (credit != null && int.TryParse(credit.HeadOutOf, out var o)) outOf = o;

                                        if (marks < 0 || marks > outOf)
                                        {
                                            return new ApiResponseDto<object> { Success = false, Message = $"Import Error: Row {row} contains invalid marks ({marks}) exceeding Max Marks ({outOf}) for {sm.Head}." };
                                        }

                                        sm.RawMarks = marks;
                                        sm.Marks = marks;
                                        
                                        // Check for Passing and Resolution
                                        var passing = 40;
                                        var passCredit = sm.CreditMaster?.Credits?.FirstOrDefault(c => c.Head == sm.Head);
                                        if (passCredit != null && int.TryParse(passCredit.HeadPass, out var p)) passing = p;

                                        if (marks >= passing)
                                        {
                                            sm.Remark = "Successful";
                                        }
                                        else if (sm.MarksMaster != null && sm.MarksMaster.ExamId.HasValue)
                                        {
                                            // Check Resolution
                                            var resolution = await _context.Resolution.FirstOrDefaultAsync(r => 
                                                r.ExamID == sm.MarksMaster.ExamId && 
                                                r.CreditID == sm.CreditsId && 
                                                r.Head == sm.Head && 
                                                !r.IsDeleted);
                                            
                                            if (resolution != null && int.TryParse(resolution.Resolution, out var resLimit) && (passing - marks) <= resLimit)
                                            {
                                                sm.Resolution = passing - marks;
                                                sm.Marks = passing;
                                                sm.Grace = (sm.Grace ?? "") + "^";
                                                sm.Remark = "Successful";
                                            }
                                            else
                                            {
                                                sm.Remark = "Unsuccessful";
                                            }
                                        }
                                        else
                                        {
                                            sm.Remark = "Unsuccessful";
                                        }
                                    }
                                    sm.UpdatedAt = DateTime.UtcNow;
                                    updatedCount++;
                                }
                            }
                        }
                    }

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
