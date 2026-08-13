using ExamAPI.DTOs;

namespace ExamAPI.Services.AtktRevalExam
{
    /// <summary>
    /// Assignment of students into a follow-up attempt: ATKT (backlog) or Revaluation.
    /// Replaces the legacy WebForm frm_atktreval_exm_assign.aspx.
    /// </summary>
    public interface IAtktRevalExamService
    {
        /// <summary>The assignment policies available for a mode -- the screen's configuration.</summary>
        Task<List<AtktPolicyDto>> GetPoliciesAsync(string? mode);

        /// <summary>Exams that may act as the source attempt (revaluation picks one explicitly).</summary>
        Task<List<AtktExamOptionDto>> GetSourceExamsAsync(Guid courseId, Guid ayid, string semester, string pattern, string mode);

        /// <summary>Exams students can be assigned into.</summary>
        Task<List<AtktExamOptionDto>> GetTargetExamsAsync(Guid courseId, Guid ayid, string semester, string mode, Guid? sourceExamId);

        /// <summary>The student x subject matrix with per-cell selectability decided by the policy.</summary>
        Task<AtktMatrixResponseDto> GetMatrixAsync(AtktMatrixRequest request);

        /// <summary>Creates, updates or removes assignments for the students in the payload.</summary>
        Task<ApiResponseDto<AtktSaveResultDto>> SaveAsync(AtktSaveRequest request);

        /// <summary>Assigns every eligible student to every subject the policy allows.</summary>
        Task<ApiResponseDto<AtktSaveResultDto>> AssignAllAsync(AtktAssignAllRequest request);

        /// <summary>Removes one student from the target exam, honouring the marks-entered guard.</summary>
        Task<ApiResponseDto<object>> DeleteAssignmentAsync(AtktDeleteRequest request);

        /// <summary>The legacy "ALL" and "Seat No" spreadsheets, rebuilt from the saved state.</summary>
        Task<(byte[] Content, string FileName)> ExportAsync(AtktExportRequest request);
    }
}
