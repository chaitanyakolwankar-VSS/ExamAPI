using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Exam;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ExamMasterController : Controller
    {
        public readonly IExamService _examService;
        public ExamMasterController(IExamService examService)
        {
            _examService = examService;
        }
        // SAVE EXAM
        [HttpPost("save-exam")]
        public async Task<IActionResult> SaveExam([FromBody] SaveExam request)
        {
            var result = await _examService.CreateExamAsync(request);
            return Ok(result);
        }
        // GET EXAMS
        [HttpGet("get-exam")]
        public async Task<IActionResult> GetExam([FromQuery] GetExam request)
        {
            var result = await _examService.GetExam(request);
            return Ok(result);
        }
        // UPDATE EXAMS
        [HttpPut("update-exam")]
        public async Task<IActionResult> UpdateExam([FromBody] UpdateExam request)
        {
            var result = await _examService.UpdateExamAsync(request);
            return Ok(result);
        }
        // DELETE EXAMS
        [HttpDelete("delete-exam")]
        public async Task<IActionResult> DeleteExam([FromBody] DeleteExam request)
        {
            var result = await _examService.DeleteExamAsync(request);
            return Ok(result);
        }
    }
}
