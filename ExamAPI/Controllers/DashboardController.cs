using System.Security.Claims;
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

        /// <summary>The caller's college from the token; a college is never taken from the query string.</summary>
        private Guid CurrentCollegeId =>
            Guid.TryParse(User.FindFirstValue("CollegeId"), out var id) ? id : Guid.Empty;

        [HttpGet("stats")]
        public async Task<IActionResult> GetDashboardStats([FromQuery] Guid ayId)
        {
            try
            {
                var stats = await _dashboardService.GetDashboardStatsAsync(CurrentCollegeId, ayId);
                return Ok(new { success = true, data = stats });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ExamAPI.Services.Common.SafeError.Message(ex) });
            }
        }


        [HttpGet("total-students")]
        public async Task<IActionResult> GetTotalStudents([FromQuery] Guid ayId)
        {
            try
            {
                var count = await _dashboardService.GetTotalStudentsAsync(CurrentCollegeId, ayId);
                return Ok(new { success = true, data = count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ExamAPI.Services.Common.SafeError.Message(ex) });
            }
        }

        [HttpGet("pass-percentage")]
        public async Task<IActionResult> GetPassPercentage([FromQuery] Guid ayId)
        {
            try
            {
                var count = await _dashboardService.GetPassPercentageAsync(CurrentCollegeId, ayId);
                return Ok(new { success = true, data = count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ExamAPI.Services.Common.SafeError.Message(ex) });
            }
        }


        [HttpGet("total-exam")]
        public async Task<IActionResult> GetTotalExamsConducted([FromQuery] Guid ayId)
        {
            try
            {
                var count = await _dashboardService.GetTotalExamsConductedAsync(CurrentCollegeId, ayId);
                return Ok(new { success = true, data = count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ExamAPI.Services.Common.SafeError.Message(ex) });
            }
        }

        [HttpGet("atkt-count")]
        public async Task<IActionResult> GetATKTStudentCount([FromQuery] Guid ayId)
        {
            try
            {
                var count = await _dashboardService.GetATKTStudentCountAsync(CurrentCollegeId, ayId);
                return Ok(new { success = true, data = count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ExamAPI.Services.Common.SafeError.Message(ex) });
            }
        }

        [HttpGet("exam-lifecycle")]
        public async Task<IActionResult> GetExamLifecycle([FromQuery] Guid ayId)
        {
            try
            {
                var data = await _dashboardService.GetExamLifecycleAsync(CurrentCollegeId, ayId);
                return Ok(new { success = true, data });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ExamAPI.Services.Common.SafeError.Message(ex) });
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
                return StatusCode(500, new { success = false, message = ExamAPI.Services.Common.SafeError.Message(ex) });
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
