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
        public async Task<IActionResult> SaveExam([FromBody] Exams request)
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
        // SEARCH EXAMS
        [HttpGet("search-exam")]
        public async Task<IActionResult> SearchExam([FromQuery] Exams request)
        {
            var result = await _examService.SearchExam(request);
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
        [HttpGet("get-resolutionexam")]
        public async Task<IActionResult> GetResolutionExam([FromQuery] GetResolutionExam request)
        {
            var result = await _examService.GetResolutionExam(request);
            return Ok(result);
        }
        [HttpGet("get-creditHeadResolution")]
        public async Task<IActionResult> GetCreditHeadResolution([FromQuery] GetCreditHeadResolutionReq request)
        {
            var result = await _examService.GetCreditHeadResolution(request);
            return Ok(result);
        }
        [HttpPost("save-creditHeadResolutionres")]
        public async Task<IActionResult> SaveCreditHeadResolutionres([FromBody] SaveCreditHeadResolutionres request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            var result = await _examService.SaveCreditHeadResolutionres(request);
            return Ok(result);
        }
        [HttpPut("update-creditHeadResolutionres")]
        public async Task<IActionResult> UpdateCreditHeadResolutionres([FromBody] SaveCreditHeadResolutionres request)
        {

            var result = await _examService.UpdateCreditHeadResolutionres(request);
            return Ok(result);
        }
    }
}
