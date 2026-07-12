using ExamAPI.DTOs;
using ExamAPI.Services.MarksEntry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ExamAPI.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class MarksEntryController : ControllerBase
    {
        private readonly IMarksEntryService _marksEntryService;

        public MarksEntryController(IMarksEntryService marksEntryService)
        {
            _marksEntryService = marksEntryService;
        }

        [HttpPost("Data")]
        public async Task<IActionResult> GetData([FromBody] MarksEntryFilterRequest request)
        {
            var collegeIdClaim = User.FindFirstValue("CollegeId");
            if (string.IsNullOrEmpty(collegeIdClaim) || !Guid.TryParse(collegeIdClaim, out var collegeId))
            {
                return Unauthorized(new ApiResponseDto<object> { Success = false, Message = "Invalid or missing CollegeId." });
            }

            var result = await _marksEntryService.GetMarksEntryDataAsync(request, collegeId);
            return Ok(result);
        }

        [HttpPost("Save")]
        public async Task<IActionResult> SaveMarks([FromBody] SaveMarksRequest request)
        {
            var collegeIdClaim = User.FindFirstValue("CollegeId");
            if (string.IsNullOrEmpty(collegeIdClaim) || !Guid.TryParse(collegeIdClaim, out var collegeId))
            {
                return Unauthorized(new ApiResponseDto<object> { Success = false, Message = "Invalid or missing CollegeId." });
            }

            var result = await _marksEntryService.SaveMarksAsync(request, collegeId);
            return Ok(result);
        }

        [HttpPost("ExportTemplate")]
        public async Task<IActionResult> ExportTemplate([FromBody] MarksEntryFilterRequest request)
        {
            var collegeIdClaim = User.FindFirstValue("CollegeId");
            if (string.IsNullOrEmpty(collegeIdClaim) || !Guid.TryParse(collegeIdClaim, out var collegeId))
            {
                return Unauthorized(new ApiResponseDto<object> { Success = false, Message = "Invalid or missing CollegeId." });
            }

            var bytes = await _marksEntryService.ExportTemplateExcelAsync(request, collegeId);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "MarksTemplate.xlsx");
        }

        [HttpPost("Import")]
        public async Task<IActionResult> ImportMarks([FromQuery] Guid examId, [FromQuery] Guid subjectId, IFormFile file)
        {
            var collegeIdClaim = User.FindFirstValue("CollegeId");
            if (string.IsNullOrEmpty(collegeIdClaim) || !Guid.TryParse(collegeIdClaim, out var collegeId))
            {
                return Unauthorized(new ApiResponseDto<object> { Success = false, Message = "Invalid or missing CollegeId." });
            }

            if (file == null || file.Length == 0)
            {
                return BadRequest(new ApiResponseDto<object> { Success = false, Message = "No file uploaded." });
            }

            using (var ms = new System.IO.MemoryStream())
            {
                await file.CopyToAsync(ms);
                var result = await _marksEntryService.ImportMarksExcelAsync(examId, subjectId, ms.ToArray(), collegeId);
                return Ok(result);
            }
        }
    }
}
