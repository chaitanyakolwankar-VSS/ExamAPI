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

        // === Pattern Endpoints ===
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
            if (!ModelState.IsValid) return BadRequest(new ApiResponseDto<object> { Success = false, Message = "Invalid data.", Data = ModelState });
            if (id != patternDto.PatternId) return BadRequest(new ApiResponseDto<object> { Success = false, Message = "Pattern ID in URL and body do not match." });
            
            var result = await _ordinanceService.UpdatePatternAsync(patternDto);
            if (!result) return NotFound(new ApiResponseDto<object> { Success = false, Message = "Pattern not found." });
            
            return Ok(new ApiResponseDto<object> { Success = true, Message = "Pattern updated successfully." });
        }

        [HttpDelete("Patterns/{id}")]
        public async Task<IActionResult> DeletePattern(Guid id)
        {
            var result = await _ordinanceService.DeletePatternAsync(id);
            if (!result) return NotFound(new ApiResponseDto<object> { Success = false, Message = "Pattern not found." });

            return Ok(new ApiResponseDto<object> { Success = true, Message = "Pattern deleted successfully." });
        }

        // === RuleSet Endpoints ===
        [HttpGet("RuleSets/ByPattern/{patternId}")]
        public async Task<IActionResult> GetRuleSetsByPattern(Guid patternId)
        {
            var ruleSetDtos = await _ordinanceService.GetRuleSetsByPatternAsync(patternId);
            return Ok(new ApiResponseDto<IEnumerable<RuleSetDto>> { Success = true, Data = ruleSetDtos, Message = "RuleSets fetched successfully." });
        }

        [HttpPost("RuleSets")]
        public async Task<IActionResult> CreateRuleSet([FromBody] RuleSetCreateDto ruleSetDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponseDto<object> { Success = false, Message = "Invalid data.", Data = ModelState });
            }

            var createdRuleSetDto = await _ordinanceService.CreateRuleSetAsync(ruleSetDto);
            // Assuming a GetRuleSetById endpoint will be created, for now returning a generic response.
            return Ok(new ApiResponseDto<RuleSetDto> { Success = true, Data = createdRuleSetDto, Message = "RuleSet created successfully." });
        }

        [HttpPut("RuleSets/{id}")]
        public async Task<IActionResult> UpdateRuleSet(Guid id, [FromBody] RuleSetUpdateDto ruleSetDto)
        {
            if (!ModelState.IsValid) return BadRequest(new ApiResponseDto<object> { Success = false, Message = "Invalid data.", Data = ModelState });
            if (id != ruleSetDto.RuleSetId) return BadRequest(new ApiResponseDto<object> { Success = false, Message = "RuleSet ID in URL and body do not match." });

            var result = await _ordinanceService.UpdateRuleSetAsync(ruleSetDto);
            if (!result) return NotFound(new ApiResponseDto<object> { Success = false, Message = "RuleSet not found." });

            return Ok(new ApiResponseDto<object> { Success = true, Message = "RuleSet updated successfully." });
        }

        [HttpDelete("RuleSets/{id}")]
        public async Task<IActionResult> DeleteRuleSet(Guid id)
        {
            var result = await _ordinanceService.DeleteRuleSetAsync(id);
            if (!result) return NotFound(new ApiResponseDto<object> { Success = false, Message = "RuleSet not found." });
            
            return Ok(new ApiResponseDto<object> { Success = true, Message = "RuleSet deleted successfully." });
        }

        // === Rule Endpoints ===
        [HttpGet("Rules/ByRuleSet/{ruleSetId}")]
        public async Task<IActionResult> GetRulesByRuleSet(Guid ruleSetId)
        {
            var ruleDtos = await _ordinanceService.GetRulesByRuleSetAsync(ruleSetId);
            return Ok(new ApiResponseDto<IEnumerable<RuleDto>> { Success = true, Data = ruleDtos, Message = "Rules fetched successfully." });
        }

        [HttpPost("Rules")]
        public async Task<IActionResult> CreateRule([FromBody] RuleCreateDto ruleDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponseDto<object> { Success = false, Message = "Invalid data.", Data = ModelState });
            }

            var createdRuleDto = await _ordinanceService.CreateRuleAsync(ruleDto);
            return Ok(new ApiResponseDto<RuleDto> { Success = true, Data = createdRuleDto, Message = "Rule created successfully." });
        }

        [HttpPut("Rules/{id}")]
        public async Task<IActionResult> UpdateRule(Guid id, [FromBody] RuleUpdateDto ruleDto)
        {
            if (!ModelState.IsValid) return BadRequest(new ApiResponseDto<object> { Success = false, Message = "Invalid data.", Data = ModelState });
            if (id != ruleDto.RuleId) return BadRequest(new ApiResponseDto<object> { Success = false, Message = "Rule ID in URL and body do not match." });

            var result = await _ordinanceService.UpdateRuleAsync(ruleDto);
            if (!result) return NotFound(new ApiResponseDto<object> { Success = false, Message = "Rule not found." });

            return Ok(new ApiResponseDto<object> { Success = true, Message = "Rule updated successfully." });
        }

        [HttpDelete("Rules/{id}")]
        public async Task<IActionResult> DeleteRule(Guid id)
        {
            var result = await _ordinanceService.DeleteRuleAsync(id);
            if (!result) return NotFound(new ApiResponseDto<object> { Success = false, Message = "Rule not found." });

            return Ok(new ApiResponseDto<object> { Success = true, Message = "Rule deleted successfully." });
        }
    }
}