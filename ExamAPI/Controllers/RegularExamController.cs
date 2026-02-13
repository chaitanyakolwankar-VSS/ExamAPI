using ExamAPI.DTOs;
using ExamAPI.Services.RegularExam;
using ExamAPI.Services.Subject;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace ExamAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RegularExamController : Controller
    {
        public readonly IRegularExamService _RegularExamService;
        public RegularExamController(IRegularExamService regularExamService)
        {
            _RegularExamService = regularExamService;
        }
        [HttpGet("get-exam")]
        public  async Task<IActionResult> Get([FromQuery] GetExam request)
        {
            var result=await _RegularExamService.GetExam(request);
            return Ok(result);
        }
        [HttpPost("get-credit")]
        public async Task<IActionResult> GetCredit([FromBody] CheckCredits request)
        {
            var result = await _RegularExamService.CheckCredits(request);
            return Ok(result);
        }
        [HttpPost("get-regularstudents")]
        public async Task<IActionResult> GetRegularStudents([FromBody] RegularExamStudents request)
        {
            var result = await _RegularExamService.GetStudents(request);
            return Ok(result);
        }
        [HttpPost("save-regular-exam-students")]
        public async Task<IActionResult> SaveRegularExamStudents(
    [FromBody] SaveRegularExamStudentsDto dto)
        {
            if (dto == null || dto.Students == null || !dto.Students.Any())
                return BadRequest("Invalid data");

          var result=  await _RegularExamService.SaveRegularExamStudents(dto);
            return Ok(result);
        }
        [HttpPut("Update-regular-exam-students")]
        public async Task<IActionResult> UpdateRegularExamStudents(
  [FromBody] SaveRegularExamStudentsDto dto)
        {
            if (dto == null || dto.Students == null || !dto.Students.Any())
                return BadRequest("Invalid data");

            var result = await _RegularExamService.UpdateRegularExamStudents(dto);
            return Ok(result);
        }

    }
}
