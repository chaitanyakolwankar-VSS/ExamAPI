using ExamAPI.DTOs;
using ExamAPI.Services.Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ExamAPI.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class OverallMarksController : ControllerBase
    {
        private readonly IResultService _resultService;

        public OverallMarksController(IResultService resultService)
        {
            _resultService = resultService;
        }

        [HttpGet("Exams")]
        public async Task<IActionResult> GetExams(Guid branchId, string semId, string pattern, Guid? ayid)
        {
            var collegeIdClaim = User.FindFirstValue("CollegeId");
            if (string.IsNullOrEmpty(collegeIdClaim) || !Guid.TryParse(collegeIdClaim, out var collegeId))
            {
                return Unauthorized(new ApiResponseDto<object> { Success = false, Message = "Invalid or missing CollegeId in token." });
            }

            var exams = await _resultService.GetExamsAsync(branchId, semId, pattern, collegeId, ayid);
            return Ok(new ApiResponseDto<IEnumerable<ExamOptionDto>> 
            { 
                Success = true, 
                Data = exams, 
                Message = "Exams fetched successfully." 
            });
        }

        [HttpPost("Process")]
        public async Task<IActionResult> ProcessResults([FromBody] ProcessResultRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponseDto<object> { Success = false, Message = "Invalid data.", Data = ModelState });
            }

            var collegeIdClaim = User.FindFirstValue("CollegeId");
            if (string.IsNullOrEmpty(collegeIdClaim) || !Guid.TryParse(collegeIdClaim, out var collegeId))
            {
                return Unauthorized(new ApiResponseDto<object> { Success = false, Message = "Invalid or missing CollegeId in token." });
            }

            var result = await _resultService.ProcessResultsAsync(request, collegeId);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpPost("Results")]
        public async Task<IActionResult> GetResults([FromBody] ProcessResultRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponseDto<object> { Success = false, Message = "Invalid data.", Data = ModelState });
            }

            var collegeIdClaim = User.FindFirstValue("CollegeId");
            if (string.IsNullOrEmpty(collegeIdClaim) || !Guid.TryParse(collegeIdClaim, out var collegeId))
            {
                return Unauthorized(new ApiResponseDto<object> { Success = false, Message = "Invalid or missing CollegeId in token." });
            }

            var result = await _resultService.GetResultsAsync(request, collegeId);
            return Ok(result);
        }

        [HttpPost("ExportExcel")]
        public async Task<IActionResult> ExportExcel([FromBody] ProcessResultRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponseDto<object> { Success = false, Message = "Invalid data.", Data = ModelState });
            }

            var collegeIdClaim = User.FindFirstValue("CollegeId");
            if (string.IsNullOrEmpty(collegeIdClaim) || !Guid.TryParse(collegeIdClaim, out var collegeId))
            {
                return Unauthorized(new ApiResponseDto<object> { Success = false, Message = "Invalid or missing CollegeId in token." });
            }

            var bytes = await _resultService.ExportResultsExcelAsync(request, collegeId);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Results_{request.ExamId}.xlsx");
        }

        [HttpPost("ExportPdf")]
        public async Task<IActionResult> ExportPdf([FromBody] ProcessResultRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponseDto<object> { Success = false, Message = "Invalid data.", Data = ModelState });
            }

            var collegeIdClaim = User.FindFirstValue("CollegeId");
            if (string.IsNullOrEmpty(collegeIdClaim) || !Guid.TryParse(collegeIdClaim, out var collegeId))
            {
                return Unauthorized(new ApiResponseDto<object> { Success = false, Message = "Invalid or missing CollegeId in token." });
            }

            var bytes = await _resultService.ExportResultsPdfAsync(request, collegeId);
            return File(bytes, "application/pdf", $"Results_{request.ExamId}.pdf");
        }
    }
}
