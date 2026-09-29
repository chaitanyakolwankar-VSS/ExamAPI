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

        [HttpGet("stats")]
        public async Task<IActionResult> GetDashboardStats([FromQuery] Guid collegeId, [FromQuery] Guid ayId)
        {
            try
            {
                var stats = await _dashboardService.GetDashboardStatsAsync(collegeId, ayId);
                return Ok(new { success = true, data = stats });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        [HttpGet("total-students")]
        public async Task<IActionResult> GetTotalStudents([FromQuery] Guid collegeId, [FromQuery] Guid ayId)
        {
            try
            {
                var count = await _dashboardService.GetTotalStudentsAsync(collegeId, ayId);
                return Ok(new { success = true, data = count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("pass-percentage")]
        public async Task<IActionResult> GetPassPercentage([FromQuery] Guid collegeId, [FromQuery] Guid ayId)
        {
            try
            {
                var count = await _dashboardService.GetPassPercentageAsync(collegeId, ayId);
                return Ok(new { success = true, data = count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        [HttpGet("total-exam")]
        public async Task<IActionResult> GetTotalExamsConducted([FromQuery] Guid collegeId, [FromQuery] Guid ayId)
        {
            try
            {
                var count = await _dashboardService.GetTotalExamsConductedAsync(collegeId, ayId);
                return Ok(new { success = true, data = count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("atkt-count")]
        public async Task<IActionResult> GetATKTStudentCount([FromQuery] Guid collegeId, [FromQuery] Guid ayId)
        {
            try
            {
                var count = await _dashboardService.GetATKTStudentCountAsync(collegeId, ayId);
                return Ok(new { success = true, data = count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("exam-lifecycle")]
        public async Task<IActionResult> GetExamLifecycle([FromQuery] Guid collegeId, [FromQuery] Guid ayId)
        {
            try
            {
                var data = await _dashboardService.GetExamLifecycleAsync(collegeId, ayId);
                return Ok(new { success = true, data });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("exam-type-distribution")]
        public async Task<IActionResult> GetExamTypeDistribution([FromQuery] Guid courseId, [FromQuery] Guid ayId)
        {
            try
            {
                var data = await _dashboardService.GetExamTypeDistributionAsync(courseId, ayId);
                return Ok(new { success = true, data });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        //old

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
