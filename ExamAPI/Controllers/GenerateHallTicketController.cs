using ExamAPI.DTOs;
using ExamAPI.Services.GenerateHallTicket;
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

        [HttpGet("get-exam")]
        public async Task<IActionResult> Get([FromQuery] GetExam request)
        {
            var result = await _GenerateHallTicketService.GetExam(request);
            return Ok(result);
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
    }
}
