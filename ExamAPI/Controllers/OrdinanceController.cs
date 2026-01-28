using Microsoft.AspNetCore.Mvc;
using ExamAPI.DTOs;
using System;
using System.Linq;
using System.Threading.Tasks;
using ExamAPI.Services.Ordinance;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace ExamAPI.Controllers
{
    [Authorize] // Secure the controller
    [Route("api/[controller]")]
    [ApiController]
    public class OrdinanceController : ControllerBase
    {
        private readonly IOrdinanceService _ordinanceService;

        public OrdinanceController(IOrdinanceService ordinanceService)
        {
            _ordinanceService = ordinanceService;
        }

        [HttpGet("Patterns")]
        public async Task<IActionResult> GetPatterns()
        {
            var patternDtos = await _ordinanceService.GetPatternsAsync();
            return Ok(new ApiResponseDto<IEnumerable<PatternDto>> { Success = true, Data = patternDtos, Message = "Patterns fetched successfully." });
        }

        [HttpGet("Patterns/{id}")]
        public async Task<IActionResult> GetPatternById(Guid id)
        {
            var patternDto = await _ordinanceService.GetPatternByIdAsync(id);
            if (patternDto == null)
            {
                return NotFound(new ApiResponseDto<object> { Success = false, Message = "Pattern not found." });
            }
            return Ok(new ApiResponseDto<PatternDto> { Success = true, Data = patternDto, Message = "Pattern fetched successfully." });
        }

        [HttpPost("Patterns")]
        public async Task<IActionResult> CreatePattern([FromBody] PatternCreateDto patternDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponseDto<object> { Success = false, Message = "Invalid data.", Data = ModelState });
            }

            // Get CollegeId from the logged-in user's token
            var collegeIdClaim = User.FindFirstValue("CollegeId");
            if (string.IsNullOrEmpty(collegeIdClaim) || !Guid.TryParse(collegeIdClaim, out var collegeId))
            {
                return Unauthorized(new ApiResponseDto<object> { Success = false, Message = "Invalid or missing CollegeId in token." });
            }

            var createdPatternDto = await _ordinanceService.CreatePatternAsync(patternDto, collegeId);
            var response = new ApiResponseDto<PatternDto> { Success = true, Data = createdPatternDto, Message = "Pattern created successfully." };
            return CreatedAtAction(nameof(GetPatternById), new { id = createdPatternDto.PatternId }, response);
        }

        [HttpPut("Patterns/{id}")]
        public async Task<IActionResult> UpdatePattern(Guid id, [FromBody] PatternUpdateDto patternDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponseDto<object> { Success = false, Message = "Invalid data.", Data = ModelState });
            }

            if (id != patternDto.PatternId)
            {
                return BadRequest(new ApiResponseDto<object> { Success = false, Message = "Pattern ID in URL and body do not match." });
            }

            var result = await _ordinanceService.UpdatePatternAsync(patternDto);
            if (!result)
            {
                return NotFound(new ApiResponseDto<object> { Success = false, Message = "Pattern not found." });
            }
            return Ok(new ApiResponseDto<object> { Success = true, Message = "Pattern updated successfully." });
        }

        [HttpDelete("Patterns/{id}")]
        public async Task<IActionResult> DeletePattern(Guid id)
        {
            var result = await _ordinanceService.DeletePatternAsync(id);
            if (!result)
            {
                return NotFound(new ApiResponseDto<object> { Success = false, Message = "Pattern not found." });
            }
            return Ok(new ApiResponseDto<object> { Success = true, Message = "Pattern deleted successfully." });
        }
    }
}
