using ExamAPI.DTOs;
using ExamAPI.Services.AssignSeatNo;
using ExamAPI.Services.RegularExam;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AssignSeatNoController : Controller
    {
        public readonly IAssignSeatNoService _AssignSeatNoService;
        public AssignSeatNoController(IAssignSeatNoService AssignSeatNoService)
        {
            _AssignSeatNoService = AssignSeatNoService;
        }
        [HttpGet("get-exam")]
        public async Task<IActionResult> Get([FromQuery] GetAssignSeatNoExam request)
        {
            var result = await _AssignSeatNoService.GetExam(request);
            return Ok(result);
        }
        [HttpGet("get-assignseatnostudents")]
        public async Task<IActionResult> GetAssignSeatNoStudents([FromQuery] GetAssignSeatNoStudents request)
        {
            var result = await _AssignSeatNoService.GetStudents(request);
            return Ok(result);
        }
        [HttpPut("update-seatno")]
        public async Task<IActionResult> UpdateSeatNo([FromBody] SaveSeatNoRequest request)
        {
            var result = await _AssignSeatNoService.UpdateSeatNo(request);
            return Ok(result);
        }
    }
}
