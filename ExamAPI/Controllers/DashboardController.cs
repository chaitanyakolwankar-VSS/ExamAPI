using ExamAPI.Services.Dashboard;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DashboardController : Controller
    {
        private readonly IDashboardService _dashboardService;
        public DashboardController(IDashboardService dashboardService)
        {
            _dashboardService = dashboardService;
        }

        [HttpGet("course-student-count")]
        public async Task<IActionResult> GetCourseStudentCount([FromQuery] Guid Ayid)
        {
            var data = await _dashboardService.GetCourseStudentCountAsync(Ayid);
            return Ok(data);
        }

        [HttpGet("semester-wise-student-count")]
        public async Task<IActionResult> GetSemesterWiseStudentCount(
            [FromQuery] Guid CourseId, [FromQuery] Guid Ayid)
        {
            var data = await _dashboardService.GetSemesterWiseStudentCountAsync(CourseId, Ayid);
            return Ok(data);
        }

        [HttpGet("pass-fail-chart")]
        public async Task<IActionResult> GetPassFailChart(
            [FromQuery] Guid CourseId, [FromQuery] Guid Ayid)
        {
            var data = await _dashboardService.GetPassFailChartAsync(CourseId, Ayid);
            return Ok(data);
        }

        [HttpGet("semester-exam-type-count")]
        public async Task<IActionResult> GetSemesterWiseExamTypeCount(
    [FromQuery] Guid CourseId, [FromQuery] Guid Ayid)
        {
            var data = await _dashboardService.GetSemesterWiseExamTypeCountAsync(CourseId, Ayid);
            return Ok(data);
        }
    }
}
