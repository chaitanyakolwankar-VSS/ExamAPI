using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Result;
using ExamAPI.Services.Result.Engine;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
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
                return new ApiResponseDto<IEnumerable<MarksEntryDataDto>> { Success = false, Message = $"Error: {ExamAPI.Services.Common.SafeError.Message(ex)}" };
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

                if (!updates.Any())
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
                var saved = 0;
                var skippedCarried = 0;
                foreach (var update in updates)
                {
                    var studentMark = studentMarks[update.StudentMarksId];

                    // A carried-forward head (ATKT/Revaluation) is not being re-sat: its mark is
                    // frozen. The grid locks the cell, and the server enforces it too.
                    if (studentMark.IsCarryForward)
                    {
                        skippedCarried++;
                        continue;
                    }

                    var error = ApplyMarkAsync(studentMark, update.Marks);
                    if (error != null)
                    {
                        return new ApiResponseDto<object> { Success = false, Message = error };
                    }

                    saved++;
                    if (studentMark.MarksMaster != null)
                    {
                        marksMasters.Add(studentMark.MarksMaster);
                    }
                }

                if (saved == 0)
                {
                    return new ApiResponseDto<object>
                    {
                        Success = false,
                        Message = "The selected head(s) are carried forward from the source attempt and locked. Nothing was saved.",
                        Data = new { saved, skippedCarryForward = skippedCarried }
                    };
                }

                foreach (var marksMaster in marksMasters)
                {
                    marksMaster.Rank = request.Rank;
                }

                // Marks entry stores raw marks only. Resolution ('^') is configured through the
                // resolution dialog and derived from ResolutionMaster when results are processed.
                await _context.SaveChangesAsync();

                var message = "Marks saved successfully.";
                if (skippedCarried > 0)
                {
                    message += $" {skippedCarried} carried-forward head(s) are locked and were left unchanged.";
                }
                return new ApiResponseDto<object>
                {
                    Success = true,
                    Message = message,
                    Data = new { saved, skippedCarryForward = skippedCarried }
                };
            }
            catch (Exception ex)
            {
                return new ApiResponseDto<object> { Success = false, Message = $"Error: {ExamAPI.Services.Common.SafeError.Message(ex)}" };
            }
        }

        /// <summary>
        /// Records what the staff typed for one head. Raw input only: any previously derived
        /// resolution/grace on the head is now stale, so it is cleared, and result processing
        /// re-derives it. Never touches a carried-forward (locked) head.
        /// </summary>
        private static string? ApplyMarkAsync(StudentMarks studentMark, string? input)
        {
            if (studentMark.IsCarryForward)
            {
                return $"{SubjectPassEvaluator.GetHeadLabel(studentMark)} is carried forward from the source attempt and cannot be edited.";
            }

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

        // ------------------------------------------------------------------------------------
        // Resolution configuration
        // ------------------------------------------------------------------------------------

        public async Task<ApiResponseDto<ResolutionConfigDto>> GetResolutionConfigAsync(ResolutionConfigRequest request, Guid collegeId)
        {
            try
            {
                var exam = await _context.Exams.FirstOrDefaultAsync(e => e.ExamId == request.ExamId && !e.IsDeleted);

                var marksRecords = await _context.MarksMasters
                    .Include(mm => mm.StudentMarks)
                        .ThenInclude(sm => sm.Subject)
                    .Include(mm => mm.StudentMarks)
                        .ThenInclude(sm => sm.CreditMaster)
                            .ThenInclude(cm => cm!.Credits)
                    .AsSplitQuery()
                    .Where(mm => mm.ExamId == request.ExamId
                        && mm.SemesterId == request.SemId
                        && mm.Pattern == request.Pattern
                        && mm.Exam != null
                        && mm.Exam.CourseId == request.BranchId
                        && mm.Student != null
                        && mm.Student.CollegeId == collegeId
                        && !mm.IsDeleted)
                    .ToListAsync();

                var resolutionRows = await _context.Resolution
                    .Where(r => r.ExamID == request.ExamId && !r.IsDeleted)
                    .ToListAsync();
                var limits = ResolutionDerivation.ParseLimits(resolutionRows);

                var subjects = new List<ResolutionConfigSubjectDto>();
                var bySubject = marksRecords
                    .SelectMany(mm => (mm.StudentMarks ?? Enumerable.Empty<StudentMarks>()).Select(sm => (Master: mm, Mark: sm)))
                    .Where(x => x.Mark.SubjectId.HasValue)
                    .GroupBy(x => x.Mark.SubjectId!.Value);

                foreach (var subjectGroup in bySubject)
                {
                    var anyMark = subjectGroup.First().Mark;
                    var creditMaster = subjectGroup.Select(x => x.Mark.CreditMaster).FirstOrDefault(cm => cm != null);

                    var headConfigs = subjectGroup
                        .Select(x => SubjectPassEvaluator.FindCredit(x.Mark))
                        .Where(c => c != null)
                        .GroupBy(c => c!.Id)
                        .Select(g => g.First()!)
                        .OrderBy(c => c.Head, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var subject = new ResolutionConfigSubjectDto
                    {
                        SubjectId = subjectGroup.Key,
                        SubjectCode = anyMark.Subject?.SubjectCode ?? string.Empty,
                        SubjectName = anyMark.Subject?.Name ?? string.Empty,
                        PassingStrategy = creditMaster?.PassingStrategy ?? PassingStrategies.HeadWise,
                        PassPercentage = creditMaster?.PassPercentage,
                        Heads = headConfigs.Select(c => new ResolutionConfigHeadDto
                        {
                            SubjectCreditId = c.Id,
                            Head = c.Head ?? string.Empty,
                            HeadType = string.IsNullOrWhiteSpace(c.HeadType) ? c.Head ?? string.Empty : c.HeadType,
                            OutOf = int.TryParse(c.HeadOutOf, out var outOf) ? outOf : 0,
                            Passing = int.TryParse(c.HeadPass, out var pass) ? pass : 0,
                            Limit = limits.TryGetValue(c.Id, out var limit) ? limit : 0
                        }).ToList()
                    };

                    var isCombined = SubjectPassEvaluator.IsCombined(creditMaster);
                    subject.SelectedHeadSubjectCreditId = isCombined
                        ? subject.Heads.FirstOrDefault(h => h.Limit > 0)?.SubjectCreditId
                        : null;
                    subject.OutOfTotal = subject.Heads.Sum(h => h.OutOf);
                    subject.RequiredToPass = isCombined && creditMaster?.PassPercentage is > 0
                        ? (int)Math.Ceiling(subject.OutOfTotal * creditMaster.PassPercentage!.Value / 100.0)
                        : subject.Heads.Sum(h => h.Passing);

                    foreach (var studentHeads in subjectGroup.GroupBy(x => x.Master.MarksId))
                    {
                        subject.StudentCount++;
                        var marks = studentHeads.Select(x => x.Mark).ToList();

                        if (marks.Any(sm => (sm.Resolution ?? 0) > 0))
                        {
                            subject.AppliedCount++;
                        }

                        // Preview from RAW marks, on detached copies so nothing is tracked or saved.
                        var preview = marks.Select(ToRawCopy).ToList();
                        if (preview.Any(sm => !sm.IsAbsent && !sm.Marks.HasValue)) continue; // not fully entered

                        var verdict = SubjectPassEvaluator.Evaluate(preview);
                        if (verdict.IsAllAbsent) continue;

                        if (!verdict.IsPassed)
                        {
                            subject.FailingCount++;
                        }

                        if (verdict.IsCombined)
                        {
                            if (!verdict.IsPassed && preview.All(sm => !sm.IsAbsent))
                            {
                                subject.Deficits.Add(verdict.Deficit);
                            }
                        }
                        else
                        {
                            foreach (var copy in preview.Where(sm => !sm.IsAbsent && !SubjectPassEvaluator.IsHeadPassed(sm)))
                            {
                                var configId = SubjectPassEvaluator.FindCredit(copy)?.Id;
                                var headDto = subject.Heads.FirstOrDefault(h => h.SubjectCreditId == configId);
                                headDto?.Deficits.Add(SubjectPassEvaluator.GetHeadPass(copy) - copy.Marks!.Value);
                            }
                        }

                        if (ResolutionDerivation.Apply(preview, limits))
                        {
                            subject.WithinLimitCount++;
                        }
                    }

                    subjects.Add(subject);
                }

                foreach (var subject in subjects)
                {
                    subject.Deficits.Sort();
                    foreach (var head in subject.Heads) head.Deficits.Sort();
                }

                return new ApiResponseDto<ResolutionConfigDto>
                {
                    Success = true,
                    Data = new ResolutionConfigDto
                    {
                        ExamId = request.ExamId,
                        IsLocked = exam?.IsLocked ?? false,
                        Subjects = subjects.OrderBy(s => s.SubjectCode).ThenBy(s => s.SubjectName).ToList()
                    }
                };
            }
            catch (Exception ex)
            {
                return new ApiResponseDto<ResolutionConfigDto> { Success = false, Message = $"Error: {ExamAPI.Services.Common.SafeError.Message(ex)}" };
            }
        }

        /// <summary>A detached copy of a head as staff typed it (raw marks, nothing derived).</summary>
        private static StudentMarks ToRawCopy(StudentMarks sm)
        {
            int? raw = sm.IsAbsent
                ? null
                : sm.RawMarks ?? (sm.Marks.HasValue ? Math.Max(0, sm.Marks.Value - (sm.Resolution ?? 0)) : null);

            return new StudentMarks
            {
                Id = sm.Id,
                Head = sm.Head,
                SubjectId = sm.SubjectId,
                CreditsId = sm.CreditsId,
                CreditMaster = sm.CreditMaster,
                IsCarryForward = sm.IsCarryForward,
                IsAbsent = sm.IsAbsent,
                RawMarks = raw,
                Marks = raw,
                // A carried head keeps the bump it carries from the source attempt.
                Resolution = sm.IsCarryForward && (sm.Resolution ?? 0) > 0 ? sm.Resolution : null,
            };
        }

        public async Task<ApiResponseDto<object>> SaveResolutionConfigAsync(SaveResolutionConfigRequest request, Guid collegeId)
        {
            try
            {
                if (request.ExamId == Guid.Empty)
                {
                    return new ApiResponseDto<object> { Success = false, Message = "Select an exam before saving resolution." };
                }
                if (request.Limits == null || request.Limits.Count == 0)
                {
                    return new ApiResponseDto<object> { Success = false, Message = "No resolution limits were supplied." };
                }

                // 1. Validate the values. Integer >= 0, no upper limit. Last entry wins per head.
                var requested = new Dictionary<Guid, int>();
                foreach (var entry in request.Limits)
                {
                    var value = entry.Limit ?? 0m;
                    if (value < 0)
                    {
                        return new ApiResponseDto<object> { Success = false, Message = "Validation Error: Resolution cannot be negative." };
                    }
                    if (value != decimal.Truncate(value))
                    {
                        return new ApiResponseDto<object> { Success = false, Message = "Validation Error: Resolution must be a whole number." };
                    }
                    if (value > int.MaxValue)
                    {
                        return new ApiResponseDto<object> { Success = false, Message = "Validation Error: Resolution is too large." };
                    }
                    if (entry.SubjectCreditId == Guid.Empty)
                    {
                        return new ApiResponseDto<object> { Success = false, Message = "Validation Error: A resolution limit is missing its head." };
                    }

                    requested[entry.SubjectCreditId] = (int)value;
                }

                var exam = await _context.Exams.FirstOrDefaultAsync(e => e.ExamId == request.ExamId && !e.IsDeleted);
                if (exam == null || (exam.CollegeId.HasValue && exam.CollegeId != collegeId))
                {
                    return new ApiResponseDto<object> { Success = false, Message = "Exam not found." };
                }
                if (exam.IsLocked)
                {
                    return new ApiResponseDto<object> { Success = false, Message = "This exam is locked. Resolution can no longer be changed." };
                }

                // 2. The heads must exist, belong to this college, and be part of this exam.
                var requestedIds = requested.Keys.ToList();
                var configuredHeads = await _context.SubjectCredits
                    .Include(c => c.CreditMaster)
                        .ThenInclude(cm => cm!.Credits)
                    .Where(c => requestedIds.Contains(c.Id) && c.CreditMaster != null)
                    .ToListAsync();
                var unknown = requestedIds.Except(configuredHeads.Select(c => c.Id)).ToList();
                if (unknown.Count > 0)
                {
                    return new ApiResponseDto<object> { Success = false, Message = "One or more heads are unavailable for the current college." };
                }

                var creditIdsInExam = (await _context.StudentMarks
                    .Where(sm => sm.CreditsId.HasValue
                        && sm.MarksMaster != null
                        && sm.MarksMaster.ExamId == request.ExamId
                        && sm.MarksMaster.Student != null
                        && sm.MarksMaster.Student.CollegeId == collegeId)
                    .Select(sm => sm.CreditsId!.Value)
                    .Distinct()
                    .ToListAsync()).ToHashSet();
                if (configuredHeads.Any(c => !c.CreditsId.HasValue || !creditIdsInExam.Contains(c.CreditsId.Value)))
                {
                    return new ApiResponseDto<object> { Success = false, Message = "One or more heads do not belong to subjects of this exam." };
                }

                // 3. Existing rows: keep one per head, drop the duplicates.
                var existingRows = await _context.Resolution
                    .Where(r => r.ExamID == request.ExamId && !r.IsDeleted)
                    .ToListAsync();
                var rowByHead = new Dictionary<Guid, ResolutionMaster>();
                var duplicates = 0;
                foreach (var group in existingRows.GroupBy(r => r.SubjectCreditID))
                {
                    var keep = ResolutionDerivation.Latest(group);
                    rowByHead[group.Key] = keep;
                    foreach (var extra in group.Where(r => r.ID != keep.ID))
                    {
                        _context.Resolution.Remove(extra);
                        duplicates++;
                    }
                }

                // 4. A combined subject condones on the combined marks through ONE head: at most one
                // of its heads may carry a limit above 0 (what is already stored counts too).
                var currentLimits = rowByHead.ToDictionary(kv => kv.Key, kv => ResolutionDerivation.ParseLimit(kv.Value.Resolution));
                foreach (var (id, limit) in requested) currentLimits[id] = limit;

                foreach (var creditMaster in configuredHeads.Select(c => c.CreditMaster!).DistinctBy(cm => cm.CreditsId))
                {
                    if (!SubjectPassEvaluator.IsCombined(creditMaster)) continue;

                    var withLimit = (creditMaster.Credits ?? new List<SubjectCredits>())
                        .Where(c => currentLimits.TryGetValue(c.Id, out var l) && l > 0)
                        .ToList();
                    if (withLimit.Count > 1)
                    {
                        return new ApiResponseDto<object>
                        {
                            Success = false,
                            Message = "Validation Error: A combined subject takes resolution on one head only. Set the limit on a single head and 0 on the others."
                        };
                    }
                }

                // 5. Upsert.
                var written = 0;
                foreach (var (subjectCreditId, limit) in requested)
                {
                    if (rowByHead.TryGetValue(subjectCreditId, out var row))
                    {
                        if (row.Resolution != limit)
                        {
                            row.Resolution = limit;
                            row.UpdatedAt = DateTime.UtcNow;
                            written++;
                        }
                        continue;
                    }

                    var head = configuredHeads.First(c => c.Id == subjectCreditId);
                    _context.Resolution.Add(new ResolutionMaster
                    {
                        ID = Guid.NewGuid(),
                        CollegeId = collegeId,
                        ExamID = request.ExamId,
                        CreditID = head.CreditsId,
                        SubjectCreditID = subjectCreditId,
                        Resolution = limit,
                        CreatedAt = DateTime.UtcNow
                    });
                    written++;
                }

                await _context.SaveChangesAsync();
                return new ApiResponseDto<object>
                {
                    Success = true,
                    Message = "Saved. Applies on next Process Results.",
                    Data = new { updated = written, duplicatesRemoved = duplicates }
                };
            }
            catch (Exception ex)
            {
                return new ApiResponseDto<object> { Success = false, Message = $"Error: {ExamAPI.Services.Common.SafeError.Message(ex)}" };
            }
        }

        public async Task<byte[]> ExportTemplateExcelAsync(MarksEntryFilterRequest request, Guid collegeId)
        {
            var dataResult = await GetMarksEntryDataAsync(request, collegeId);
            if (!dataResult.Success || dataResult.Data == null || !dataResult.Data.Any())
            {
                return Array.Empty<byte>();
            }

            var marksData = dataResult.Data.ToList();
            var subject = await _context.SubjectMasters.FindAsync(request.SubjectId);
            var exam = await _context.Exams.FindAsync(request.ExamId);

            using (var workbook = ExamAPI.Services.Report.ExcelStyles.NewWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Marks Entry");
                ExamAPI.Services.Report.ExcelStyles.NormalMargins(worksheet);

                // Title
                worksheet.Range("A1:F1").Merge();
                worksheet.Cell("A1").Value = "MARKS ENTRY TEMPLATE";
                worksheet.Cell("A1").Style.Font.FontSize = 16;
                worksheet.Cell("A1").Style.Font.Bold = true;
                worksheet.Cell("A1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                worksheet.Cell("A1").Style.Font.FontColor = XLColor.FromColor(Color.DarkBlue);

                // Header Information
                worksheet.Cell("A3").Value = "Exam:";
                worksheet.Cell("B3").Value = exam?.Name ?? "N/A";
                worksheet.Cell("A4").Value = "Subject:";
                worksheet.Cell("B4").Value = subject?.Name ?? "N/A";
                worksheet.Range("A3:A4").Style.Font.Bold = true;

                // Column Headers
                worksheet.Cell(6, 1).Value = "Seat No";
                worksheet.Cell(6, 2).Value = "Student ID";
                worksheet.Cell(6, 3).Value = "Student Name";

                var heads = marksData.First().Heads.OrderBy(h => h.HeadName).ToList();
                int col = 4;
                foreach (var head in heads)
                {
                    worksheet.Cell(6, col).Value = $"{head.HeadName} (Out Of: {head.OutOf})";
                    worksheet.Cell(5, col).Value = head.HeadName; // Raw head name for matching
                    worksheet.Cell(5, col).Style.Font.FontColor = XLColor.White; // Hide it by making it white or keep it visible

                    // Hidden column for StudentMarksId
                    worksheet.Cell(6, col + 1).Value = $"{head.HeadName}_ID";
                    worksheet.Column(col + 1).Hide();
                    col += 2;
                }

                // Style the Header Row
                var header = worksheet.Range(6, 1, 6, col - 1).Style;
                header.Font.Bold = true;
                header.Font.FontColor = XLColor.White;
                header.Fill.BackgroundColor = XLColor.FromArgb(41, 128, 185); // Professional Blue
                header.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                header.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                // Data
                int row = 7;
                foreach (var student in marksData)
                {
                    worksheet.Cell(row, 1).Value = student.SeatNo;
                    worksheet.Cell(row, 2).Value = student.StudentId;
                    worksheet.Cell(row, 3).Value = student.StudentName;

                    int sCol = 4;
                    foreach (var head in student.Heads.OrderBy(h => h.HeadName))
                    {
                        worksheet.Cell(row, sCol).Value = head.Marks;
                        worksheet.Cell(row, sCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        // Highlight missing/empty marks with light yellow for data entry focus
                        if (string.IsNullOrEmpty(head.Marks))
                        {
                            worksheet.Cell(row, sCol).Style.Fill.BackgroundColor = XLColor.FromArgb(255, 253, 208); // Cream/Light Yellow
                        }

                        worksheet.Cell(row, sCol + 1).Value = head.StudentMarksId.ToString();
                        sCol += 2;
                    }
                    row++;
                }

                // Apply borders to the entire data table
                ExamAPI.Services.Report.ExcelStyles.ThinBorders(worksheet.Range(6, 1, row - 1, col - 1).Style, XLColor.FromColor(Color.Gray));

                // Add Data Validations to columns
                int vCol = 4;
                foreach (var head in heads)
                {
                    var validation = worksheet.Range(7, vCol, Math.Max(7, row - 1), vCol).CreateDataValidation();
                    var cellAddress = worksheet.Cell(7, vCol).Address.ToStringRelative();

                    validation.Custom($"OR(EXACT({cellAddress}, \"Ab\"), EXACT({cellAddress}, \"ab\"), AND(ISNUMBER({cellAddress}), {cellAddress}>=0, {cellAddress}<={head.OutOf}))");
                    validation.ShowErrorMessage = true;
                    validation.ErrorStyle = XLErrorStyle.Stop;
                    validation.ErrorTitle = "Invalid Marks Entry";
                    validation.ErrorMessage = $"Marks must be between 0 and {head.OutOf}, or 'Ab' for absent.";

                    vCol += 2;
                }

                ExamAPI.Services.Report.ExcelStyles.AutoFit(worksheet, 1, col - 1, 1, row - 1);
                return ExamAPI.Services.Report.ExcelStyles.ToBytes(workbook);
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

                using (var stream = new MemoryStream(fileBytes))
                using (var workbook = new XLWorkbook(stream))
                {
                    var worksheet = workbook.Worksheet(1);
                    int rowCount = worksheet.LastRowUsed()?.RowNumber() ?? 0;
                    int colCount = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;

                    // Identify Head columns and their ID columns
                    var headColumns = new List<(int MarkCol, int IdCol)>();
                    for (int col = 4; col <= colCount; col++)
                    {
                        var headerValue = ExamAPI.Services.Report.ExcelStyles.Text(worksheet.Cell(6, col));
                        if (headerValue != null && headerValue.EndsWith("_ID"))
                        {
                            headColumns.Add((col - 1, col));
                        }
                    }

                    int updatedCount = 0;
                    int skippedCarried = 0;
                    for (int row = 7; row <= rowCount; row++)
                    {
                        foreach (var (markCol, idCol) in headColumns)
                        {
                            var idValue = ExamAPI.Services.Report.ExcelStyles.Text(worksheet.Cell(row, idCol));
                            if (Guid.TryParse(idValue, out Guid studentMarksId))
                            {
                                var markValue = ExamAPI.Services.Report.ExcelStyles.Text(worksheet.Cell(row, markCol))?.Trim();
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

                                // Carried-forward heads are frozen; skip them and report the count.
                                if (sm.IsCarryForward)
                                {
                                    skippedCarried++;
                                    continue;
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

                    // An import is marks entry by another route: raw marks only. Resolution is derived
                    // from ResolutionMaster when results are processed.
                    await _context.SaveChangesAsync();

                    var importMessage = $"Successfully imported marks for {updatedCount} records.";
                    if (skippedCarried > 0)
                    {
                        importMessage += $" {skippedCarried} carried-forward head(s) are locked and were skipped.";
                    }
                    return new ApiResponseDto<object>
                    {
                        Success = true,
                        Message = importMessage,
                        Data = new { updated = updatedCount, skippedCarryForward = skippedCarried }
                    };
                }
            }
            catch (Exception ex)
            {
                return new ApiResponseDto<object> { Success = false, Message = $"Import failed: {ExamAPI.Services.Common.SafeError.Message(ex)}" };
            }
        }
    }
}
