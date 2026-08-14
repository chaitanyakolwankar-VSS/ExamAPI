using ExamAPI.DTOs;

namespace ExamAPI.Services.Dashboard
{
    public interface IDashboardService
    {
        Task<List<CourseStudentCountDTO>> GetCourseStudentCountAsync(Guid ayid);
        Task<List<SemesterStudentCountDTO>> GetSemesterWiseStudentCountAsync(Guid courseId, Guid ayid);
        Task<List<PassFailChartDTO>> GetPassFailChartAsync(Guid courseId, Guid ayId);
        Task<List<SemesterExamTypeCountDTO>> GetSemesterWiseExamTypeCountAsync(Guid courseId, Guid ayId);
    }
}
