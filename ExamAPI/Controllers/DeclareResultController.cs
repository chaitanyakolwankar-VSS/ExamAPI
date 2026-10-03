using ExamAPI.DTOs;
using ExamAPI.Services.DeclareResult;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DeclareResultController : ControllerBase
    {
        private readonly IDResultService _DeclareResultService;

        public DeclareResultController(IDResultService declareResultService)
        {
            _DeclareResultService = declareResultService;
        }

        [HttpGet("get-exam")]
        public async Task<IActionResult> Get([FromQuery] GetDeclareExam request)
        {
            var result = await _DeclareResultService.GetExam(request);
            return Ok(result);
        }

        [HttpGet("get-table-exam")]
        public async Task<IActionResult> GetExamTable([FromQuery] DeclareExamTable request)
        {
            var result = await _DeclareResultService.GetTableExam(request);
            return Ok(result);
        }

        [HttpPut("toggle-declare-result")]
        public async Task<IActionResult> ToggleDeclare([FromBody] ToggleDeclareResultDTO request)
        {
            if (request.IsDeclare && request.DeclareDate == default)
                return BadRequest(new { message = "Declare date is required to declare a result." });

            var success = await _DeclareResultService.ToggleDeclare(request);
            if (!success)
                return NotFound(new { message = "Exam not found for the given course/year/semester." });

            return Ok(new { message = "Declare status updated successfully." });
        }

    }
}
