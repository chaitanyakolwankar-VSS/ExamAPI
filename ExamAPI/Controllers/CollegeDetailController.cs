using ExamAPI.DTOs;
using ExamAPI.Services.Auth;
using ExamAPI.Services.CollegeDetail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CollegeDetailController : ControllerBase
    {

        private readonly ICollegeDetailService _collegeDetailService;

        public CollegeDetailController(ICollegeDetailService collegeDetailService)
        {
            _collegeDetailService = collegeDetailService;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var result = await _collegeDetailService.GetAsync();
            if (result == null)
                return NotFound();

            return Ok(result);
        }



        // Only the platform (developer) login creates colleges (DEC-17).
        [HttpPost]
        [Authorize(Policy = AccessPolicies.PlatformAdmin)]
        [Consumes("multipart/form-data")]

        public async Task<IActionResult> Create(CreateCollegeDTO dto)
        {
            var id = await _collegeDetailService.CreateAsync(dto);
            return Ok(new { CollegeId = id });
        }

        // A college admin edits their own college (the tenant filter hides all others); a platform admin any.
        [HttpPut("{id}")]
        [Authorize(Policy = AccessPolicies.CollegeAdmin)]
        public async Task<IActionResult> Update(Guid id, [FromForm] CreateCollegeDTO dto)
        {
            var result = await _collegeDetailService.UpdateAsync(id, dto);
            return Ok(new { CollegeId = result, Message = "Updated Successfully" });
        }

    }
}
