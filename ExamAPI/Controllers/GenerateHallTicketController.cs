using ExamAPI.DTOs;
using ExamAPI.Services.Auth;
using ExamAPI.Services.GenerateHallTicket;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GenerateHallTicketController : Controller
    {
        public readonly IGenerateHallTicketService _GenerateHallTicketService;
        public GenerateHallTicketController(IGenerateHallTicketService generateHallTicketService)
        {
            _GenerateHallTicketService = generateHallTicketService;
        }

        [HttpGet("get-halltickectsubjects")]
        public async Task<IActionResult> HallTickectSubjects([FromQuery] HallTicketSubjectsRequest request)
        {
            var result = await _GenerateHallTicketService.GetHallTicketSubject(request);
            return Ok(result);
        }
        [HttpPost("save-timetable")]
        public async Task<IActionResult> SaveTimeTable([FromBody] SaveTimeTable request)
        {
            var result = await _GenerateHallTicketService.SaveTimeTable(request);
            return Ok(result);
        }


        [HttpGet("get-timetable-all")]
        public async Task<IActionResult> GetTimeTableAll([FromQuery] Guid examId)
        {
            var result = await _GenerateHallTicketService.GetTimeTableAll(examId);
            return result == null ? NotFound() : Ok(result);
        }

        [HttpGet("get-halltickectstudentsdata")]
        public async Task<IActionResult> HallTickectStudentData([FromQuery] StudentHallTicketDataRequest request)
        {
            var result = await _GenerateHallTicketService.HallTickectStudentData(request);
            return Ok(result);
        }
        [HttpGet("get-hallticketcollegedata")]
        public async Task<IActionResult> HallTicketCollegeData()
        {
            var result = await _GenerateHallTicketService.HallTicketCollegeData();
            return Ok(result);
        }

        /// <summary>The college's hall ticket instructions (any signed-in staff).</summary>
        [HttpGet("instructions")]
        public async Task<IActionResult> Instructions()
        {
            return Ok(await _GenerateHallTicketService.GetInstructionsAsync());
        }

        /// <summary>Switches the instructions on or off and saves their text. College admin only.</summary>
        [HttpPut("instructions")]
        [Authorize(Policy = AccessPolicies.CollegeAdmin)]
        public async Task<IActionResult> SaveInstructions([FromBody] HallTicketInstructionsSave dto)
        {
            try
            {
                return Ok(await _GenerateHallTicketService.SaveInstructionsAsync(dto));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
