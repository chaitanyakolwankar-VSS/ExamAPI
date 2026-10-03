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

        [HttpGet("get-table-exam")]
        public async Task<IActionResult> GetExamTable([FromQuery] DeclareExamTable request)
        {
            var result = await _ReleaseHallticketService.GetTableExam(request);
            return Ok(result);
        }

        [HttpPost("toggle-release")]
        public async Task<IActionResult> ToggleReleaseHallTicket([FromBody] ToggleReleaseHallTicketDTO request)
        {
            var result = await _ReleaseHallticketService.ToggleReleaseHallTicket(request);
            return Ok(result);
        }
    }
}
