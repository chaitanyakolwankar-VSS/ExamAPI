using ExamAPI.DTOs;

namespace ExamAPI.Services.Lookup
{
    public interface ILookupService
    {
        Task<LookupBootstrapDto> GetBootstrapAsync(CancellationToken ct = default);

        /// <summary>Null when <paramref name="purpose"/> is unknown.</summary>
        Task<List<LookupExamDto>?> GetExamsAsync(Guid courseId, Guid ayid, string purpose, string? semester, CancellationToken ct = default);

        Task<List<LookupSubjectDto>> GetSubjectsAsync(Guid courseId, string? pattern, string? semester, CancellationToken ct = default);
    }
}
