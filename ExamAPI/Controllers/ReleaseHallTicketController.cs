using ExamAPI.DTOs;
using ExamAPI.Services.ReleaseHallTicket;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ReleaseHallTicketController : ControllerBase
    {
        private readonly IReleaseHallticketService _ReleaseHallticketService;
        public ReleaseHallTicketController(IReleaseHallticketService releaseHallticketService)
        {
            _ReleaseHallticketService = releaseHallticketService;
        }

        /// <summary>Exams that can have a hall ticket for the course/semester, with their release status (no DeclareResult row needed).</summary>
        [HttpGet("get-exam")]
        public async Task<IActionResult> GetExam([FromQuery] GetDeclareExam request)
        {
            var result = await _ReleaseHallticketService.GetExam(request);
            return Ok(result);
        }

        [HttpGet("get-table-exam")]
        public async Task<IActionResult> GetExamTable([FromQuery] DeclareExamTable request)
        {
            var result = await _ReleaseHallticketService.GetTableExam(request);
            return Ok(result);
        }

        [HttpPost("toggle-release")]
        public async Task<IActionResult> ToggleReleaseHallTicket([FromBody] ToggleReleaseHallTicketDTO request)
        {
            if (request.ReleaseHallTicket && (request.HallTicketDeclareDate == null || request.HallTicketDeclareDate == default(DateTime)))
                return BadRequest(new { message = "A release date is required to release a hall ticket." });

            var success = await _ReleaseHallticketService.ToggleReleaseHallTicket(request);
            if (!success)
                return NotFound(new { message = "Exam not found for the given course/year/semester." });

            return Ok(success);
        }
    }
}
