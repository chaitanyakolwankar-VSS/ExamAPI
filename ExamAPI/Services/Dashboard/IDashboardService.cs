using ExamAPI.DTOs;

namespace ExamAPI.Services.Dashboard
{
    public interface IDashboardService
    {
        // New methods for dashboard stats
        Task<DashboardStatsDTO> GetDashboardStatsAsync(Guid collegeId, Guid ayId);
        Task<int> GetTotalStudentsAsync(Guid collegeId, Guid ayId);
        Task<decimal> GetPassPercentageAsync(Guid collegeId, Guid ayId);
        Task<int> GetTotalExamsConductedAsync(Guid collegeId, Guid ayId);
        Task<int> GetATKTStudentCountAsync(Guid collegeId, Guid ayId);
        Task<List<ExamLifecycleDTO>> GetExamLifecycleAsync(Guid collegeId, Guid ayId);
        Task<List<ExamTypeDistributionDTO>> GetExamTypeDistributionAsync(Guid courseId, Guid ayId);

        //Old
        Task<List<CourseStudentCountDTO>> GetCourseStudentCountAsync(Guid ayid);
        Task<List<SemesterStudentCountDTO>> GetSemesterWiseStudentCountAsync(Guid courseId, Guid ayid);
        Task<List<PassFailChartDTO>> GetPassFailChartAsync(Guid courseId, Guid ayId);
        Task<List<SemesterExamTypeCountDTO>> GetSemesterWiseExamTypeCountAsync(Guid courseId, Guid ayId);
    }
}
