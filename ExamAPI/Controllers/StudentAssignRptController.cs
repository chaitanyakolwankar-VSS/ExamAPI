using ExamAPI.DTOs;
using ExamAPI.Services.StudentAssignRpt;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StudentAssignRptController : ControllerBase
    {
        public readonly IStudentAssignRptService _studentAssignRpt;
        private readonly ILogger<StudentAssignRptController> _logger;

        public StudentAssignRptController(IStudentAssignRptService studentAssignRpt, ILogger<StudentAssignRptController> logger)
        {
            _studentAssignRpt = studentAssignRpt;
            _logger = logger;
        }

        [HttpPost("GetReport")]
        public async Task<IActionResult> GetReport([FromBody] StudentAssignRptDto request)
        {
            if (request.CourseId == Guid.Empty || request.ExamId == Guid.Empty || string.IsNullOrWhiteSpace(request.Pattern) || string.IsNullOrWhiteSpace(request.Semester))
            {
                return BadRequest("Course,Pattern,Semester and Exam are required.");
            }

            try
            {
                var data = await _studentAssignRpt.GetReportAsync(request);
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load Student Assign Report");
                return StatusCode(500, "Something went wrong while fetching the report");
            }
        }

        [HttpPost("GetCreditReport")]
        public async Task<IActionResult> GetCreditReport([FromBody] StudentAssignRptDto request)
        {
            if (request.CourseId == Guid.Empty || request.ExamId == Guid.Empty ||
                string.IsNullOrWhiteSpace(request.Pattern) ||
                string.IsNullOrWhiteSpace(request.Semester))
            {
                return BadRequest("Course, Pattern, Semester and Exam are required.");
            }

            var data = await _studentAssignRpt.GetCreditReportAsync(request);
            return Ok(data);
        }

    }
}
