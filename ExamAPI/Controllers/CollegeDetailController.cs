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

        [HttpPost]
        public async Task<IActionResult> Create(CreateCollegeDTO dto)
        {
            var id = await _collegeDetailService.CreateAsync(dto);
            return Ok(new { CollegeId = id });
        }
    }
}
