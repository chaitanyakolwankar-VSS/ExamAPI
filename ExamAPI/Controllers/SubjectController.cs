using ExamAPI.DTOs;
using ExamAPI.Services.Subject;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;

namespace ExamAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SubjectController : ControllerBase
    {
        private readonly ISubjectService _subjectService;

        public SubjectController(ISubjectService subjectService)
        {
            _subjectService = subjectService;
        }

        // SAVE SUBJECT
        [HttpPost("save-subjects")]
        public async Task<IActionResult> SaveSubject([FromBody] CreateSubjectDto request)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { success = false, message = "Invalid data" });

            var result = await _subjectService.CreateSubjectAsync(request);
            return Ok(result);
        }
        // GET SUBJECTS
        [HttpGet("get-subjects")]
        public async Task<IActionResult> GetSubjects([FromQuery] GetSubjectReqDtos dto)
        {
            var result = await _subjectService.GetSubjectsAsync(dto);
            return Ok(result);
        }
        [HttpPost("save-credits")]
        public async Task<IActionResult> SaveCredits([FromBody] SaveCreditsDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { success = false, message = "Invalid data" });
            }
            var result = await _subjectService.SaveCreditAsync(dto);
            return Ok(new { success = result.Success, message = result.Message });
        }
        // GET SUBJECTS
        [HttpGet("get-credits")]
        public async Task<IActionResult> GetCredits([FromQuery] GetCredits dto)
        {
            var result = await _subjectService.GetSubjectCreditAsync(dto);
            return Ok(result);
        }
        [HttpPut("Update-credits")]
        public async Task<IActionResult> UpdateCredits([FromBody] SaveCreditsDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { success = false, message = "Invalid data" });
            }
            var result = await _subjectService.UpdateCreditAsync(dto);
            return Ok(new { success = result.Success, message =result.Message});
        }
        [HttpDelete("Delete-credits")]
        public async Task<IActionResult> DeleteCredits([FromBody] DeleteCreditDto dto)
        {
            var result = await _subjectService.DeleteCreditAsync(dto);
            return Ok(new { success = result.Success, message = result.Message });
        }
        [HttpDelete("Delete-subject")]
        public async Task<IActionResult> DeleteSubject([FromBody] DeleteSubjectDto dto)
        {
            var result = await _subjectService.DeleteSubjectAsync(dto);
            return Ok(new { success = result.Success, message = result.Message });
        }
        [HttpGet("get-previous-credits")]
        public async Task<IActionResult> GetPreviousCredits([FromQuery] PreviousCredits dto)
        {
            var result = await _subjectService.GetPreviousCreditAsync(dto);
            return Ok(new { success = result.Success, message = result.Message });
        }
        [HttpPost("copy-previous-credits")]
        public async Task<IActionResult> SavePreviousCredits([FromBody] PreviousCredits dto)
        {
            var result = await _subjectService.SavePreviousCreditAsync(dto);
            return Ok(result);
        }
        [HttpGet("check-credits")]
        public async Task<IActionResult> CheckCredits([FromQuery] GetCredits dto)
        {
            var result = await _subjectService.CheckCreditAsync(dto);
            return Ok(new { success = result.Success, message = result.Message });
        }
        [HttpPost("verify-access")]
        public async Task<IActionResult> VerifyCreditAccess(
    [FromBody] VerifyCreditAccessDto dto)
        {
            var result = await _subjectService.VerifyCreditAccess(dto);
            return Ok(new { success = result.Success, message = result.Message });
        }

    }
}
