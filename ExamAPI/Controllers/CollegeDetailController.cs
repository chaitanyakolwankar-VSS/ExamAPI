using ExamAPI.DTOs;
using ExamAPI.Services.CollegeDetail;
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



        [HttpPost]
        [Consumes("multipart/form-data")]

        public async Task<IActionResult> Create(CreateCollegeDTO dto)
        {
            var id = await _collegeDetailService.CreateAsync(dto);
            return Ok(new { CollegeId = id });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, [FromForm] CreateCollegeDTO dto)
        {
            var result = await _collegeDetailService.UpdateAsync(id, dto);
            return Ok(new { CollegeId = result, Message = "Updated Successfully" });
        }

    }
}
