using ExamAPI.DTOs;
using ExamAPI.Services.ATKTCummulativeReport;
using ExamAPI.Services.AttendenceReport;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AttendenceReportController : Controller
    {
        public readonly IAttendenceReportService _AttendenceReport;

        public AttendenceReportController(IAttendenceReportService AttendenceReportService)
        {
            _AttendenceReport = AttendenceReportService;
        }
        [HttpGet("get-exam")]
        public async Task<IActionResult> Get([FromQuery] ReportExamRequest request)
        {
            var result = await _AttendenceReport.GetExam(request);
            return Ok(result);
        }
        [HttpGet("GetSubjectStudentCount")]
        public async Task<IActionResult> GetSubjectStudentCount(
           [FromQuery] AttendenceReportRequestDTO request)
        {
            var result = await _AttendenceReport.GetSubjectStudentCount(request);

            return Ok(result);
        }
        [HttpGet("GetSubjectSeatNo")]
        public async Task<IActionResult> GetSubjectSeatNo(
          [FromQuery] AttendenceSeatNoRequestDTO request)
        {
            var result = await _AttendenceReport.GetSubjectSeatNo(request);

            return Ok(result);
        }
    }
}
