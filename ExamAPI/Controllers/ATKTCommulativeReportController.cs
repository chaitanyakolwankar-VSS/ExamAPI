using ExamAPI.DTOs;
using ExamAPI.Services.ATKTCummulativeReport;
using ExamAPI.Services.RegularExam;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ATKTCommulativeReportController : Controller
    {
        public readonly IATKTCummulativeReportService _ATKTCummulativeReportService;

        public ATKTCommulativeReportController(IATKTCummulativeReportService ATKTCummulativeReportService)
        {
            _ATKTCummulativeReportService = ATKTCummulativeReportService;
        }
        [HttpGet("get-exam")]
        public async Task<IActionResult> Get([FromQuery] ReportExamRequest request)
        {
            var result = await _ATKTCummulativeReportService.GetExam(request);
            return Ok(result);
        }
        [HttpGet("get-HeadType")]
        public async Task<IActionResult> GetHeadType([FromQuery] HeadType request)
        {
            var result = await _ATKTCummulativeReportService.GetHeadType(request);
            return Ok(result);
        }
        [HttpGet("get-AtktReportData")]
        public async Task<IActionResult> GetAtktReportData([FromQuery] AtktReportRequest request)
        {
            var result = await _ATKTCummulativeReportService.GetReportData(request);
            return Ok(result);
        }
    }
}
