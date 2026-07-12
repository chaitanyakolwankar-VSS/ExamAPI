using ExamAPI.DTOs;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ExamAPI.Services.MarksEntry
{
    public interface IMarksEntryService
    {
        Task<ApiResponseDto<IEnumerable<MarksEntryDataDto>>> GetMarksEntryDataAsync(MarksEntryFilterRequest request, Guid collegeId);
        Task<ApiResponseDto<object>> SaveMarksAsync(SaveMarksRequest request, Guid collegeId);
        Task<byte[]> ExportTemplateExcelAsync(MarksEntryFilterRequest request, Guid collegeId);
        Task<ApiResponseDto<object>> ImportMarksExcelAsync(Guid examId, Guid subjectId, byte[] fileBytes, Guid collegeId);
    }
}
